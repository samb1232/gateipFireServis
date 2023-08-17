using System;
using System.Net.Http;
using System.IO;
using System.ServiceProcess;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections.Generic;

namespace WebPortalService
{

    
    public class WebPortalService : ServiceBase
    {


        private readonly string settingsFilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"..\..\settings.json");

        private string baseUrl;
        private string authFolder;
        private string doorLockAllFolder;
        private string doorUnlockAllFolder;
        private string doorGetListFolder;

        private string httpUsername;
        private string httpPassword;

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
            LoadSettings();
            LoginToPortal();
            CheckFireDoorStatusPeriodically().GetAwaiter().GetResult();
        }

        protected override void OnStop()
        {
            cancellationTokenSource.Cancel();
            httpClient.Dispose();
        }

        private void LoadSettings()
        {

            if (!File.Exists(settingsFilePath))
            {
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
            httpPassword         = (string)settings["httpPassword"];
            fireDoorName         = (string)settings["fireDoorName"];
            timerIntervalSeconds = (int)settings["timerIntervalSeconds"];
            doorOpenState        = (int)settings["openState"];
            doorNormalState      = (int)settings["normalState"];
        }

        private void LoginToPortal()
        {
            string passmd5 = EncryptPasswordByMD5(httpPassword);
            
            var requestData = new
            {
                UserName = httpUsername,
                // PasswordHash = passmd5
                PasswordHash = "AAB1CA4FCCEEC333E47424A4C689585CE4197AF7B7" 
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = httpClient.PostAsync(baseUrl + authFolder, content).GetAwaiter().GetResult();


            if (response.IsSuccessStatusCode)
            {
                string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JObject responseData = JObject.Parse(responseBody);

                JToken userSIDToken = responseData?["UserSID"];

                if (userSIDToken != null)
                {
                    userSID = (string)userSIDToken;
                }
                else
                {
                    throw new HttpRequestException($"Request failed. Recieved userSID is None");
                }
            }
            else
            {
                throw new HttpRequestException($"Request failed. {response}");
            }
        }

        static string CalculateMD5Hash(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    builder.Append(hashBytes[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }
        private string EncryptPasswordByMD5(string password)
        {
            string smt = "0e60612bea"; // GenerateRandomString(8 + (int)(8 * new Random().NextDouble()));
            string finalPass = CalculateMD5Hash(
                    CalculateMD5Hash(
                        CalculateMD5Hash(
                            password.ToUpper() + "F593B01C562548C6B7A31B30884BDE53").ToUpper()
                            + smt.ToUpper()).ToUpper() 
                            + smt.ToUpper()).ToUpper();
            return finalPass;
        }




        private async Task CheckFireDoorStatusPeriodically()
        {
            int OPEN_STATE = 1;
            int NORMAL_STATE = 0;
            int NOT_IMLEMENTED_STATE = -1;

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
                    return NOT_IMLEMENTED_STATE;
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

                // Пауза между проверками
                await Task.Delay(TimeSpan.FromSeconds(timerIntervalSeconds));
            }
        }

        private async Task<bool> CallDoorUnlockAll()
        {
            var requestData = new
            {
                Language = "",
                UserSID = userSID
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorUnlockAllFolder, content);


            return response.IsSuccessStatusCode;
        }

        private async Task<bool> CallDoorLockAll()
        {
            var requestData = new
            {
                Language = "",
                UserSID = userSID
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorLockAllFolder, content);
            return response.IsSuccessStatusCode;
        }

        static void Main(string[] args)
        {
            Run(new WebPortalService());
        }
    }
}
