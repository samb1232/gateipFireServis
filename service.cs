using System;
using System.Net.Http;
using System.IO;
using System.ServiceProcess;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.Generic;
using Serilog;

namespace WebPortalService
{
    public class WebPortalService : ServiceBase
    {
        private readonly string settingsFilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "settings.json");

        private string baseUrl;
        private string authFolder;
        private string doorLockAllFolder;
        private string doorUnlockAllFolder;
        private string doorGetListFolder;

        private string httpUsername;
        private string PasswordHash;

        private string fireDoorName;
        private int timerIntervalSeconds;
        private int doorOpenState;
        private int doorNormalState;

        private readonly HttpClient httpClient;
        private CancellationTokenSource cancellationTokenSource;


        private string userSID;





        public WebPortalService()
        {
            ServiceName = "WebPortalService";
            CanStop = true;
            cancellationTokenSource = new CancellationTokenSource();
            httpClient = new HttpClient();
        }

        protected override void OnStart(string[] args)
        {
            
                Log.Information("������ ������ �������");
                LoadSettings();
                LoginToPortal().GetAwaiter().GetResult();
                CheckFireDoorStatusPeriodically().GetAwaiter().GetResult();
            
        }

        protected override void OnStop()
        {
            Log.Information("��������� ������ �������");
            cancellationTokenSource.Cancel();
            httpClient.Dispose();
            Log.CloseAndFlush();
        }

        private void LoadSettings()
        {
            Log.Information("������ ������ ����� ��������");
            if (!File.Exists(settingsFilePath))
            {
                Log.Error($"������, ���� �������� \"{settingsFilePath}\" �� ������");

                throw new FileNotFoundException("Settings file not found.");
            }

            string settingsJson = File.ReadAllText(settingsFilePath);
            JObject settings = JObject.Parse(settingsJson);


            baseUrl              = (string)settings["baseUrl"];
            authFolder           = (string)settings["authFolder"];
            doorLockAllFolder    = (string)settings["doorLockAllFolder"];
            doorUnlockAllFolder  = (string)settings["doorUnlockAllFolder"];
            doorGetListFolder    = (string)settings["doorGetListFolder"];
            httpUsername         = (string)settings["httpUsername"];
            PasswordHash         = (string)settings["PasswordHash"];
            fireDoorName         = (string)settings["fireDoorName"];
            timerIntervalSeconds = (int)settings["timerIntervalSeconds"];
            doorOpenState        = (int)settings["openState"];
            doorNormalState      = (int)settings["normalState"];

            Log.Information("��������� �� ����� ������� �������");
        }

        private async Task LoginToPortal()
        {
            
            var requestData = new
            {
                UserName = httpUsername,
                PasswordHash
            };
            StringContent content;
            HttpResponseMessage response = null;
            do
            {
                Log.Information("������� ����������� � �������");
                try
                {
                    content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    response = await httpClient.PostAsync(baseUrl + authFolder, content);
                    Log.Information($"������ ����������� � �������: {response.StatusCode}");
                }
                catch (Exception)
                {
                    Log.Warning("������ ��� ����������� � �������. ����������� ��������� ������� �����������");
                    await Task.Delay(10000); // �������� 10 ������
                    continue; 
                }
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(10000);//�������� 10 ������
                }
            } while ((response == null || !response.IsSuccessStatusCode) && !cancellationTokenSource.Token.IsCancellationRequested);

            string responseBody = await response.Content.ReadAsStringAsync();
            JObject responseData = JObject.Parse(responseBody);
            
            JToken userSIDToken = responseData?["UserSID"];
            
            if (userSIDToken != null)
            {
                Log.Information("����������� � ������� �������, ���� userSID ��������");
                userSID = (string)userSIDToken;
            }
            else
            {
                Log.Error("������ ��������� ���� userSID");
                throw new HttpRequestException($"Request failed. Recieved userSID is null");
            }
        }


        private async Task CheckFireDoorStatusPeriodically()
        {
            Log.Information("������������� �������� ����� FireDoor");
            int OPEN_STATE = 1;
            int NORMAL_STATE = 0;
            int NOT_IMPLEMENTED_STATE = -1;

            async Task<int> CheckFireDoorStatus()
            {
                
                var requestData = new
                {
                    Language            = "",
                    UserSID             = userSID,
                    SubscriptionEnabled = true,
                    Limit               = 0,
                    StartToken          = 0
                };

                StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorGetListFolder, content);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Http request failed with status code: {response.StatusCode}");
                }

                string responseContent = await response.Content.ReadAsStringAsync();
                Dictionary<string, object> dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);
                JArray doorsArray = (JArray)dictionary["Door"];

                //Looking for fireDoor in the Jarray of all doors
                JToken fireDoor = null;
                foreach (JToken door in doorsArray)
                {
                    if ((string)door["Name"] == fireDoorName)
                    {
                        fireDoor = door;
                        break;
                    }
                }
                if (fireDoor == null)
                {
                    throw new Exception("FireDoor object not found. Make sure that FireDoor name in settings.json is correct.");
                }

                if ((int)fireDoor["HardwareState"] == doorOpenState)
                {
                    return OPEN_STATE;
                }
                else if ((int)fireDoor["HardwareState"] == doorNormalState)
                {
                    return NORMAL_STATE;
                }
                else
                {
                    return NOT_IMPLEMENTED_STATE;
                }
            }

            int prevStatus = NORMAL_STATE;
            while (!cancellationTokenSource.Token.IsCancellationRequested)      
            {                

                int fireDoorStatus = await CheckFireDoorStatus();

                if (fireDoorStatus == OPEN_STATE && prevStatus == NORMAL_STATE)
                {
                    await CallDoorUnlockAll();
                    prevStatus = OPEN_STATE;
                }
                else if (fireDoorStatus == NORMAL_STATE && prevStatus == OPEN_STATE)
                {
                    await CallDoorLockAll();
                    prevStatus = NORMAL_STATE;
                }

                // ����� ����� ����������
                await Task.Delay(TimeSpan.FromSeconds(timerIntervalSeconds));
            }
        }

        private async Task CallDoorUnlockAll()
        {
            Log.Information("�������� ������� ������� CallDoorUnlockAll");
            var requestData = new
            {
                Language = "",
                UserSID = userSID
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorUnlockAllFolder, content);

            Log.Information($"������� CallDoorUnlockAll �������. Status code: {response.StatusCode}");
        }

        private async Task CallDoorLockAll()
        {
            Log.Information("�������� ������� ������� CallDoorLockAll");
            var requestData = new
            {
                Language = "",
                UserSID = userSID
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorLockAllFolder, content);
            Log.Information($"������� CallDoorLockAll �������. Status code: {response.StatusCode}");
        }

        private async Task TurnOffInInterval(int seconds)
        {
            await Task.Delay(seconds * 1000);
            cancellationTokenSource.Cancel();
        }

        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

            if (Environment.UserInteractive)
            {
                WebPortalService service = new WebPortalService();
                Task task1 = service.TurnOffInInterval(60); // shutdown in 1 minute 
                service.OnStart(args);
                service.OnStop();
                task1.GetAwaiter();
            }
            else
            {
                try
                {
                    Run(new WebPortalService());
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                    Log.CloseAndFlush();
                }
                
            }
        }
    }
}
