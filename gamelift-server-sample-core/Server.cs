using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using Aws.GameLift.Server;
using LiteNetLib;
using LiteNetLib.Utils;
using log4net;

namespace gamelift_server_sample_core
{
    internal class Peers
    {
        private readonly ConcurrentDictionary<IPEndPoint, NetPeer> _dict
            = new ConcurrentDictionary<IPEndPoint, NetPeer>();

        public bool Add(NetPeer peer)
        {
            return _dict.TryAdd(peer.EndPoint, peer);
        }

        public void ForEach(Action<NetPeer> action)
        {
            foreach (var d in _dict)
            {
                action(d.Value);
            }
        }

        public void Delete(IPEndPoint e)
        {
            _dict.TryRemove(e, out _);
        }

        public bool IsEmpty()
        {
            return _dict.Count == 0;
        }
    }

    internal class PlayerEndPoints
    {
        private readonly ConcurrentDictionary<IPEndPoint, string> _dict
            = new ConcurrentDictionary<IPEndPoint, string>();

        public bool Add(IPEndPoint endPoint, string playerSessionId)
        {
            return _dict.TryAdd(endPoint, playerSessionId);
        }

        public string Get(IPEndPoint endPoint)
        {
            _dict.TryGetValue(endPoint, out var playerSessionId);
            return playerSessionId;
        }

        public void Delete(IPEndPoint endPoint)
        {
            _dict.TryRemove(endPoint, out _);
        }

        public void ForEach(Action<string> action)
        {
            foreach (var d in _dict)
            {
                action(d.Value);
            }
        }
    }

    public class Server
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Program));

        private bool _isRunning;
        private Aws.GameLift.Server.Model.GameSession _session;

        private readonly Peers _peerList;
        private readonly PlayerEndPoints _playerEndPoints;

        public Server()
        {
            _isRunning = true;
            _peerList = new Peers();
            _playerEndPoints = new PlayerEndPoints();
        }

        public static bool InitSdk()
        {
            LogInfo("call InitSDK");
            var init = GameLiftServerAPI.InitSDK();
            if (init.Success || init.Error == null) return true;
            LogError("init error: {0}", init.Error);
            return false;
        }

        public int Run()
        {
            var listener = new EventBasedNetListener();
            var server = new NetManager(listener);

            for (var i = 0; i < 10; i++)
            {
                if (!server.Start())
                {
                    LogError("cannot launch server");
                    return 1;
                }

                // 10000 - 60000 の範囲外の場合は再度ポートを割り当ててみる
                var port = server.LocalPort;
                if (10000 <= port && port <= 60000)
                {
                    break;
                }

                LogInfo("out of port range: {0}, restart server", port);
                server.Stop();
            }

            if (!server.IsRunning)
            {
                LogError("cannot launch server");
                return 2;
            }

            var listenPort = server.LocalPort;
            LogInfo("listen on: {0}", listenPort);

            listener.ConnectionRequestEvent += request =>
            {
                // PlayerSessionId を接続時に受け取って GameLift に問い合わせる
                var pid = request.Data.GetString();
                LogInfo("playerSessionId: {0}", pid);
                var res = GameLiftServerAPI.AcceptPlayerSession(pid);
                if (!res.Success)
                {
                    request.Reject();
                    return;
                }

                if (!_playerEndPoints.Add(request.RemoteEndPoint, pid))
                {
                    request.Reject();
                    return;
                }

                request.Accept();
            };
            listener.PeerConnectedEvent += peer =>
            {
                if (!_peerList.Add(peer))
                {
                    peer.Disconnect();
                }

                LogInfo("connected: {0}", peer.EndPoint);
                if (_peerList.IsEmpty()) return;

                var json = CreateAdminMessage($"[{peer.EndPoint}]さんが入りました").Serialize();
                var w = new NetDataWriter();
                _peerList.ForEach(p =>
                {
                    w.Reset();
                    w.Put(json);
                    p.Send(w, DeliveryMethod.ReliableOrdered);
                });
            };
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                var pid = _playerEndPoints.Get(peer.EndPoint);
                if (!string.IsNullOrEmpty(pid))
                {
                    GameLiftServerAPI.RemovePlayerSession(pid);
                    LogInfo("remove player session: {0}", pid);
                    _playerEndPoints.Delete(peer.EndPoint);
                }

                _peerList.Delete(peer.EndPoint);
                Console.WriteLine("disconnected: {0}, {1}", peer.EndPoint, info.Reason);
                if (!_peerList.IsEmpty())
                {
                    var json = CreateAdminMessage($"[{peer.EndPoint}]さんが抜けました").Serialize();
                    var w = new NetDataWriter();
                    _peerList.ForEach(p =>
                    {
                        w.Reset();
                        w.Put(json);
                        p.Send(w, DeliveryMethod.ReliableOrdered);
                    });
                }

                // 全員セッションから抜けたら終了する
                GameLiftServerAPI.TerminateGameSession();
                LogInfo("terminate game session: {0}", _session.GameSessionId);
            };
            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                // クライアントから受け取ったデータをセッションのメンバーにブロードキャストする
                var body = reader.GetString();
                var msg = new Message {Body = body, Name = peer.EndPoint.ToString()};
                var json = msg.Serialize();
                var w = new NetDataWriter();
                _peerList.ForEach(p =>
                {
                    w.Reset();
                    w.Put(json);
                    p.Send(w, DeliveryMethod.ReliableOrdered);
                });
            };

            var processParameters = new ProcessParameters
            {
                Port = listenPort,
                OnStartGameSession = session =>
                {
                    LogInfo("start session: {0}", session.GameSessionId);
                    GameLiftServerAPI.ActivateGameSession();
                    _session = session;
                },
                OnProcessTerminate = () =>
                {
                    _playerEndPoints.ForEach(pid => GameLiftServerAPI.RemovePlayerSession(pid));
                    _peerList.ForEach(p => p.Disconnect());
                    GameLiftServerAPI.ProcessEnding();
                    _isRunning = false;
                }
            };

            var outcome = GameLiftServerAPI.ProcessReady(processParameters);
            LogInfo("process ready: {0}", outcome.Success);
            if (!outcome.Success && outcome.Error != null)
            {
                LogError("process ready error: {0}", outcome.Error.ToString());
                return 3;
            }

            while (_isRunning)
            {
                server.PollEvents();
                Thread.Sleep(15);
            }

            server.Stop();
            return 0;
        }

        private static Message CreateAdminMessage(string msg)
        {
            return new Message {Name = "admin", Body = msg};
        }

        private static void LogError(string format, object arg0)
        {
            Console.WriteLine(format, arg0);
            Logger.ErrorFormat(format, arg0);
        }

        private static void LogError(string msg)
        {
            Console.WriteLine(msg);
            Logger.Error(msg);
        }

        private static void LogInfo(string format, object arg0)
        {
            Console.WriteLine(format, arg0);
            Logger.InfoFormat(format, arg0);
        }

        private static void LogInfo(string msg)
        {
            Console.WriteLine(msg);
            Logger.Info(msg);
        }
    }
}