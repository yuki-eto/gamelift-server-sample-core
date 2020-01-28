using System;
using CommandLine;
using log4net.Repository.Hierarchy;

namespace gamelift_server_sample_core
{
    [Verb("server", HelpText = "server mode")]
    internal class ServerOptions
    {
    }

    [Verb("client", HelpText = "client mode")]
    internal class ClientOptions
    {
        [Option('s', "session-id", Required = false, HelpText = "GameLift SessionID")]
        public string Sid { get; set; }

        [Option("local", Default = false, HelpText = "using local server")]
        public bool LocalMode { get; set; }

        [Option("search", Default = false, HelpText = "search session mode")]
        public bool SearchMode { get; set; }

        [Option('f', "fleet-id", Required = false, HelpText = "GameLift fleet id")]
        public string FleetId { get; set; }
    }

    internal static class Program
    {
        public static int Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            return Parser.Default.ParseArguments<ServerOptions, ClientOptions>(args)
                .MapResult(
                    (ServerOptions opts) => RunServer(),
                    (ClientOptions opts) => RunClient(opts),
                    errs => 1
                );
        }

        private static int RunServer()
        {
            Console.WriteLine("running on server mode");
            var server = new Server();
            return server.Run();
        }

        private static int RunClient(ClientOptions opts)
        {
            Console.WriteLine("running on client mode");
            var client = new Client();
            // 開いてるセッションを探して入る
            if (opts.SearchMode)
            {
                var session = client.SearchSession(opts.FleetId);
                if (session != null) return client.DescribeAndRun(session.GameSessionId);

                Console.WriteLine("cannot find available session, create new session");
                return client.CreateAndRun(opts.FleetId);
            }

            // セッションIDを直で指定して入る
            var fleetId = opts.FleetId;
            if (opts.LocalMode)
            {
                client.UseLocalServer();
                fleetId = "fleet-1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d";
            }

            return string.IsNullOrEmpty(opts.Sid) ? client.CreateAndRun(fleetId) : client.DescribeAndRun(opts.Sid);
        }
    }
}