using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.GameLift;
using Amazon.GameLift.Model;
using Aws.GameLift;
using LiteNetLib;
using LiteNetLib.Utils;
using log4net;

namespace gamelift_server_sample_core
{
    public class Client
    {
        private const string RoomName = "test-a-1";

        private bool _isRunning = true;
        private AmazonGameLiftClient _glClient;

        public Client()
        {
            var conf = new AmazonGameLiftConfig {RegionEndpoint = RegionEndpoint.APNortheast1};
            _glClient = new AmazonGameLiftClient(conf);
            Console.CancelKeyPress += (sender, args) =>
            {
                args.Cancel = true;
                _isRunning = false;
            };
        }

        public void UseLocalServer()
        {
            var conf = new AmazonGameLiftConfig {ServiceURL = "http://localhost:9080"};
            _glClient = new AmazonGameLiftClient(conf);
        }

        private int RunBySession(GameSession session)
        {
            var playerSession = CreatePlayerSession(session);
            if (playerSession == null)
            {
                return 1;
            }

            var playerSessionId = playerSession.PlayerSessionId;
            var client = GetListener();
            if (!client.IsRunning)
            {
                return 2;
            }

            var peer = Connect(session, playerSessionId, client);
            MainLoop(client, peer);
            return 0;
        }

        public int CreateAndRun(string fleetId)
        {
            var sid = "gsess-" + Guid.NewGuid();
            var session = CreateGameSession(fleetId, sid);
            if (session == null)
            {
                return 1;
            }

            WaitForActivate(ref session);
            return RunBySession(session);
        }

        public int DescribeAndRun(string sid)
        {
            var req = new DescribeGameSessionsRequest
            {
                GameSessionId = sid
            };
            var res = _glClient.DescribeGameSessionsAsync(req).Result;
            if (res.GameSessions.Count < 1)
            {
                return 1;
            }

            var session = res.GameSessions.First();
            return RunBySession(session);
        }

        public GameSession SearchSession(string fleetId)
        {
            var req = new SearchGameSessionsRequest
            {
                FleetId = fleetId,
                FilterExpression =
                    $"gameSessionProperties.roomName = '{RoomName}' AND hasAvailablePlayerSessions = true"
            };
            var res = _glClient.SearchGameSessionsAsync(req).Result;
            if (res.GameSessions.Count < 1)
            {
                return null;
            }

            foreach (var s in res.GameSessions)
            {
                Console.WriteLine($"session: {s.GameSessionId}, {s.CreationTime}");
            }

            return res.GameSessions.First();
        }

        private GameSession CreateGameSession(string fleetId, string sid)
        {
            var props = new List<GameProperty> {new GameProperty {Key = "roomName", Value = RoomName}};
            var createReq = new CreateGameSessionRequest
            {
                Name = RoomName,
                FleetId = fleetId,
                IdempotencyToken = sid,
                MaximumPlayerSessionCount = 4,
                GameProperties = props,
            };
            var res = _glClient.CreateGameSessionAsync(createReq).Result;
            return res.GameSession;
        }

        private void WaitForActivate(ref GameSession session)
        {
            for (var i = 0; i < 10; i++)
            {
                var descRequest = new DescribeGameSessionsRequest {GameSessionId = session.GameSessionId};
                var res = _glClient.DescribeGameSessionsAsync(descRequest).Result;
                session = res.GameSessions.First();
                if (session.Status != GameSessionStatus.ACTIVATING)
                {
                    break;
                }

                Console.WriteLine("wait for activate: {0}({1})", session.GameSessionId, session.Status);
                Thread.Sleep(1000);
            }
        }

        private PlayerSession CreatePlayerSession(GameSession session)
        {
            var pid = Guid.NewGuid().ToString();
            var psReq = new CreatePlayerSessionRequest {GameSessionId = session.GameSessionId, PlayerId = pid};
            var task = _glClient.CreatePlayerSessionAsync(psReq);
            try
            {
                var cpRes = task.Result;
                var pSess = cpRes.PlayerSession;
                Console.WriteLine("create player session: {0}", pSess.PlayerSessionId);
                return pSess;
            }
            catch (AggregateException e)
            {
                var exception = e.InnerExceptions.First();
                if (exception.GetType() != typeof(GameSessionFullException)) throw;
                Console.WriteLine("session[{0}] is full: {1}", session.GameSessionId, pid);
                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine("cannot create player session: {0}, {1}", pid, e);
                return null;
            }
        }

        private NetManager GetListener()
        {
            var listener = new EventBasedNetListener();
            listener.NetworkErrorEvent += (point, error) =>
            {
                Console.WriteLine("err: {0}", error.ToString());
                _isRunning = false;
            };
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                Console.WriteLine("info: {0}", info.Reason);
                _isRunning = false;
            };
            listener.NetworkReceiveEvent += (peer, reader, method) =>
            {
                var json = reader.GetString();
                var msg = Message.FromString(json);
                Console.WriteLine();
                Console.WriteLine($"({msg.Name})>> {msg.Body}");
                Console.Write(">> ");
                reader.Recycle();
            };

            var client = new NetManager(listener);
            client.Start();
            return client;
        }

        private static NetPeer Connect(GameSession session, string playerSessionId, NetManager client)
        {
            var target = IPEndPoint.Parse($"{session.IpAddress}:{session.Port}");
            Console.WriteLine("target: {0}", target);
            return client.Connect(target, playerSessionId);
        }

        private void MainLoop(NetManager client, NetPeer peer)
        {
            var writer = new NetDataWriter();
            Task.Run(() =>
            {
                while (_isRunning)
                {
//                    var input = ReadLine.Read(">> ");
//                    if (string.IsNullOrWhiteSpace(input))
//                    {
//                        continue;
//                    }
//
//                    if (input.Trim(new[] {' '}) == "/ping")
//                    {
//                        Console.WriteLine($"<ping: {peer.Ping}ms>");
//                        continue;
//                    }

                    writer.Reset();
                    writer.Put(DateTime.Now.ToString(CultureInfo.CurrentCulture));
                    peer.Send(writer, DeliveryMethod.Unreliable);
                    Thread.Sleep(33);
                }
            });

            while (_isRunning)
            {
                client.PollEvents();
                Thread.Sleep(15);
            }

            client.Stop();
        }
    }
}