using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.GameLift;
using Amazon.GameLift.Model;
using LiteNetLib;
using LiteNetLib.Utils;

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

        public int CreateAndRun(string fleetId)
        {
            var sid = "gsess-" + Guid.NewGuid();
            var session = CreateGameSession(fleetId, sid);
            if (session == null)
            {
                return 1;
            }

            WaitForActivate(ref session);

            var playerSession = CreatePlayerSession(session);
            if (playerSession == null)
            {
                return 1;
            }

            MainLoop(session, playerSession);
            return 0;
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
            var playerSession = CreatePlayerSession(session);
            if (playerSession == null)
            {
                return 1;
            }

            MainLoop(session, playerSession);
            return 0;
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
            try
            {
                var cpRes = _glClient.CreatePlayerSessionAsync(psReq).Result;
                var pSess = cpRes.PlayerSession;
                Console.WriteLine("create player session: {0}", pSess.PlayerSessionId);
                return pSess;
            }
            catch (GameSessionFullException)
            {
                Console.WriteLine("session[{0}] is full: {1}", session.GameSessionId, pid);
                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine("cannot create player session: {0}, {1}", pid, e);
                return null;
            }
        }

        private void MainLoop(GameSession session, PlayerSession playerSession)
        {
            var port = session.Port;
            var host = session.IpAddress;
            Console.WriteLine("host: {0}, port: {1}", host, port);

            var listener = new EventBasedNetListener();
            var client = new NetManager(listener);
            client.Start();
            var server = client.Connect(host, port, playerSession.PlayerSessionId);

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
                Console.WriteLine("");
                Console.WriteLine($"({msg.Name})>> {msg.Body}");
                Console.Write(">> ");
                reader.Recycle();
            };

            var writer = new NetDataWriter();
            Task.Run(() =>
            {
                while (_isRunning)
                {
                    Console.Write(">> ");
                    var s = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        continue;
                    }

                    writer.Reset();
                    writer.Put(s);
                    server.Send(writer, DeliveryMethod.Unreliable);
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