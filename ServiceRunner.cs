using Topshelf;

namespace GateIPFireService
{
    public class ServiceRunner { 
        public static void Main(string[] args)
        {
            var exitCode = HostFactory.Run( application =>
            {
                application.Service<FireDoorService>(service =>
                {
                    service.ConstructUsing(fdService => new FireDoorService());
                    service.WhenStarted(fdService => fdService.Start());
                    service.WhenStopped(fdService => fdService.Stop());
                });

                application.RunAsLocalSystem();

                application.SetServiceName("FireDoorService");
                application.SetDisplayName("FireDoor Service");
                application.SetDescription("FireDoor Service for handling fire door behavior");

            });

            int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
            Environment.ExitCode = exitCodeValue;
        }
    }
}