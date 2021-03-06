// Copyright (c) Microsoft. All rights reserved.

namespace IotEdgeQuickstart
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using IotEdgeQuickstart.Details;
    using McMaster.Extensions.CommandLineUtils;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;

    [Command(
        Name = "IotEdgeQuickstart",
        Description = "An app which automates the \"Quickstart\" tutorial (https://docs.microsoft.com/en-us/azure/iot-edge/quickstart-linux)",
        ExtendedHelpText = @"
Environment Variables:
  Most options in this command override environment variables. In other words,
  the value of the corresponding environment variable will be used unless the
  option is specified on the command line.

  Option                    Environment variable
  --bootstrapper-archive    bootstrapperArchivePath
  --connection-string       iothubConnectionString
  --eventhub-endpoint       eventhubCompatibleEndpointWithEntityPath
  --password                registryPassword
  --registry                registryAddress
  --tag                     imageTag
  --username                registryUser

Defaults:
  All options to this command have defaults. If an option is not specified and
  its corresponding environment variable is not defined, then the default will
  be used.

  Option                    Default value
  --bootstrapper            'iotedged'
  --bootstrapper-archive    no path (archive is installed from apt or pypi)
  --connection-string       get the value from Key Vault
  --device-id               an auto-generated unique identifier
  --edge-hostname           'quickstart'
  --eventhub-endpoint       get the value from Key Vault
  --leave-running           none (or 'all' if given as a switch)
  --password                anonymous, or Key Vault if --registry is specified
  --registry                mcr.microsoft.com (anonymous)
  --tag                     '1.0'
  --use-http                if --bootstrapper=iotedged then use Unix Domain
                            Sockets, otherwise N/A
                            switch form uses local IP address as hostname
  --username                anonymous, or Key Vault if --registry is specified
  --no-deployment           deploy Edge Hub and temperature sensor modules
"
        )]
    [HelpOption]
    class Program
    {
        // ReSharper disable once UnusedMember.Local
        static int Main(string[] args) => CommandLineApplication.ExecuteAsync<Program>(args).Result;

        [Option("-a|--bootstrapper-archive <path>", Description = "Path to bootstrapper archive")]
        public string BootstrapperArchivePath { get; } = Environment.GetEnvironmentVariable("bootstrapperArchivePath");

        [Option("-b|--bootstrapper=<iotedged/iotedgectl>", CommandOptionType.SingleValue, Description = "Which bootstrapper to use")]
        public BootstrapperType BootstrapperType { get; } = BootstrapperType.Iotedged;

        [Option("-c|--connection-string <value>", Description = "IoT Hub connection string (hub-scoped, e.g. iothubowner)")]
        public string IotHubConnectionString { get; } = Environment.GetEnvironmentVariable("iothubConnectionString");

        [Option("-d|--device-id", Description = "Edge device identifier registered with IoT Hub")]
        public string DeviceId { get; } = $"iot-edge-quickstart-{Guid.NewGuid()}";

        [Option("-e|--eventhub-endpoint <value>", Description = "Event Hub-compatible endpoint for IoT Hub, including EntityPath")]
        public string EventHubCompatibleEndpointWithEntityPath { get; } = Environment.GetEnvironmentVariable("eventhubCompatibleEndpointWithEntityPath");

        [Option("-h|--use-http=<hostname>", Description = "Modules talk to iotedged via tcp instead of unix domain socket")]
        public (bool useHttp, string hostname) UseHttp { get; } = (false, string.Empty);

        [Option("-n|--edge-hostname", Description = "Edge device's hostname")]
        public string EdgeHostname { get; } = "quickstart";

        [Option("-p|--password <password>", Description = "Docker registry password")]
        public string RegistryPassword { get; } = Environment.GetEnvironmentVariable("registryPassword");

        [Option("-r|--registry <hostname>", Description = "Hostname of Docker registry used to pull images")]
        public string RegistryAddress { get; } = Environment.GetEnvironmentVariable("registryAddress");

        [Option("-t|--tag <value>", Description = "Tag to append when pulling images")]
        public string ImageTag { get; } = Environment.GetEnvironmentVariable("imageTag");

        [Option("-u|--username <username>", Description = "Docker registry username")]
        public string RegistryUser { get; } = Environment.GetEnvironmentVariable("registryUser");

        [Option("--leave-running=<All/Core/None>", CommandOptionType.SingleOrNoValue, Description = "Leave IoT Edge running when the app is finished")]
        public LeaveRunning LeaveRunning { get; } = LeaveRunning.None;

        [Option("--no-deployment", CommandOptionType.NoValue, Description = "Don't deploy Edge Hub and temperature sensor modules")]
        public bool NoDeployment { get; } = false;

        // ReSharper disable once UnusedMember.Local
        async Task<int> OnExecuteAsync()
        {
            if (this.BootstrapperType == BootstrapperType.Iotedged &&
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("IotEdgeQuickstart parameter '--bootstrapper' does not yet support 'iotedged' on Windows. Please specify 'iotedgectl' instead.");
                return 1;
            }

            try
            {
                string address = this.RegistryAddress;
                string user = this.RegistryUser;
                string password = this.RegistryPassword;

                if (address != null && user == null && password == null)
                {
                    (user, password) = await this.RegistryArgsFromSecret(address);
                }

                Option<RegistryCredentials> credentials = address != null && user != null && password != null
                    ? Option.Some(new RegistryCredentials(address, user, password))
                    : Option.None<RegistryCredentials>();

                IBootstrapper bootstrapper;
                switch (this.BootstrapperType)
                {
                    case BootstrapperType.Iotedged:
                        {
                            (bool useHttp, string hostname) = this.UseHttp;
                            Option<HttpUris> uris = useHttp
                                ? Option.Some(string.IsNullOrEmpty(hostname) ? new HttpUris() : new HttpUris(hostname))
                                : Option.None<HttpUris>();
                            bootstrapper = new Iotedged(this.BootstrapperArchivePath, credentials, uris);
                        }
                        break;
                    case BootstrapperType.Iotedgectl:
                        bootstrapper = new Iotedgectl(this.BootstrapperArchivePath, credentials);
                        break;
                    default:
                        throw new ArgumentException("Unknown BootstrapperType");
                }

                string connectionString = this.IotHubConnectionString ??
                    await SecretsHelper.GetSecretFromConfigKey("iotHubConnStrKey");

                string endpoint = this.EventHubCompatibleEndpointWithEntityPath ??
                    await SecretsHelper.GetSecretFromConfigKey("eventHubConnStrKey");

                string tag = this.ImageTag ?? "1.0";

                var test = new Quickstart(
                    bootstrapper,
                    credentials,
                    connectionString,
                    endpoint,
                    tag,
                    this.DeviceId,
                    this.EdgeHostname,
                    this.LeaveRunning,
                    this.NoDeployment);
                await test.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }

            Console.WriteLine("Success!");
            return 0;
        }

        async Task<(string, string)> RegistryArgsFromSecret(string address)
        {
            // Expects our Key Vault to contain a secret with the following properties:
            //  key   - based on registry hostname (e.g.,
            //          edgerelease.azurecr.io => edgerelease-azurecr-io)
            //  value - "<user> <password>" (separated by a space)

            string key = address.Replace('.', '-');
            string value = await SecretsHelper.GetSecret(key);
            string[] vals = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (vals[0], vals[1]);
        }
    }

    public enum BootstrapperType
    {
        Iotedged,
        Iotedgectl
    }

    public enum LeaveRunning
    {
        All,  // don't clean up anything
        Core, // remove modules/identities except Edge Agent & Hub
        None  // iotedgectl stop, uninstall, remove device identity
    }
}
