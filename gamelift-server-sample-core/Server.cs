using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using Aws.GameLift;
using Aws.GameLift.Server;
using LiteNetLib;
using LiteNetLib.Utils;
using log4net;

namespace gamelift_server_sample_core
{
    internal class Peers
    {
        private readonly ConcurrentDictionary<IPEndPoint, NetPeer> _dict =
            new ConcurrentDictionary<IPEndPoint, NetPeer>();

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
        private readonly ConcurrentDictionary<IPEndPoint, string>
            _dict = new ConcurrentDictionary<IPEndPoint, string>();

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
        private readonly ILog _logger = Logger.GetLogger(typeof(Server));

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

        private static Message CreateAdminMessage(string msg)
        {
            return new Message {Name = "admin", Body = msg};
        }

        private void OnConnectionRequestEvent(ConnectionRequest request)
        {
            // PlayerSessionId を接続時に受け取って GameLift に問い合わせる
            var pid = request.Data.GetString();
            _logger.InfoFormat("playerSessionId: {0}", pid);
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
        }

        private void OnConnectedEvent(NetPeer peer)
        {
            if (!_peerList.Add(peer))
            {
                peer.Disconnect();
            }

            _logger.InfoFormat("connected: {0}", peer.EndPoint);
            if (_peerList.IsEmpty()) return;

            var json = CreateAdminMessage($"[{peer.EndPoint}]さんが入りました").Serialize();
            var w = new NetDataWriter();
            _peerList.ForEach(p =>
            {
                w.Reset();
                w.Put(json);
                p.Send(w, DeliveryMethod.ReliableOrdered);
            });
        }

        private void OnDisconnectedEvent(NetPeer peer, DisconnectInfo info)
        {
            var pid = _playerEndPoints.Get(peer.EndPoint);
            if (!string.IsNullOrEmpty(pid))
            {
                GameLiftServerAPI.RemovePlayerSession(pid);
                _logger.InfoFormat("remove player session: {0}", pid);
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
                return;
            }

            // 全員セッションから抜けたら終了する
            GameLiftServerAPI.TerminateGameSession();
            _logger.InfoFormat("terminate game session: {0}", _session.GameSessionId);
        }

        private void OnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, DeliveryMethod _)
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
        }

        private NetManager ListenServer()
        {
            var listener = new EventBasedNetListener();
            listener.ConnectionRequestEvent += OnConnectionRequestEvent;
            listener.PeerConnectedEvent += OnConnectedEvent;
            listener.PeerDisconnectedEvent += OnDisconnectedEvent;
            listener.NetworkReceiveEvent += OnNetworkReceiveEvent;

            var server = new NetManager(listener);

            for (var i = 0; i < 10; i++)
            {
                if (!server.Start())
                {
                    continue;
                }

                // 10000 - 60000 の範囲外の場合は再度ポートを割り当ててみる
                var port = server.LocalPort;
                if (10000 <= port && port <= 60000)
                {
                    break;
                }

                _logger.InfoFormat("out of port range: {0}, restart server", port);
                server.Stop();
            }

            return server;
        }

        private void ProcessEnding()
        {
            _playerEndPoints.ForEach(pid => GameLiftServerAPI.RemovePlayerSession(pid));
            _peerList.ForEach(p => p.Disconnect());
            GameLiftServerAPI.ProcessEnding();
        }

        private bool ProcessReady(int listenPort, out GameLiftError err)
        {
            var processParameters = new ProcessParameters
            {
                Port = listenPort,
                OnStartGameSession = session =>
                {
                    _logger.InfoFormat("start session: {0}", session.GameSessionId);
                    GameLiftServerAPI.ActivateGameSession();
                    _session = session;
                },
                OnProcessTerminate = () => { _isRunning = false; }
            };

            var outcome = GameLiftServerAPI.ProcessReady(processParameters);
            err = outcome.Error;
            return outcome.Success;
        }

        public int Run()
        {
            _logger.Info("call InitSDK");
            var init = GameLiftServerAPI.InitSDK();
            if (!init.Success)
            {
                _logger.ErrorFormat("init error: {0}", init.Error);
                return 1;
            }

            var server = ListenServer();
            if (!server.IsRunning)
            {
                _logger.ErrorFormat("cannot launch server");
                return 2;
            }

            var listenPort = server.LocalPort;
            _logger.InfoFormat("listen on: {0}", listenPort);
            GlobalContext.Properties["ListenPort"] = listenPort;

            if (!ProcessReady(listenPort, out var err))
            {
                _logger.ErrorFormat("cannot ready to process: {0}", err);
                return 3;
            }

            while (_isRunning)
            {
                server.PollEvents();
                Thread.Sleep(15);
            }

            _logger.InfoFormat("process ending: {0}", listenPort);
            ProcessEnding();
            server.Stop();
            return 0;
        }
    }
}