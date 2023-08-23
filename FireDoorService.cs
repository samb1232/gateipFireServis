using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Serilog;
using System.Reflection;
using System.Text;

namespace GateIPFireService
{
    public class FireDoorService
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

        private HttpClient httpClient;
        private CancellationTokenSource cancellationTokenSource;


        private string userSID;

        public FireDoorService()
        {
            Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "logs\\log.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

            Log.Information("Инициализация сервиса");
            cancellationTokenSource = new CancellationTokenSource();
            httpClient = new HttpClient();
            LoadSettings(); 
        }

        public async void Start()
        {
            Log.Information("Начало работы сервиса");
            await LoginToPortal();
            Task pollingTask = CheckFireDoorStatusPeriodically();

        }

        public void Stop()
        {
            Log.Information("Окончание работы сервиса");
            cancellationTokenSource.Cancel();
            httpClient.Dispose();
            Log.CloseAndFlush();
        }

        private void LoadSettings()
        {
            Log.Information("Начало чтения файла настроек");
            if (!File.Exists(settingsFilePath))
            {
                Log.Error($"Ошибка, файл настроек \"{settingsFilePath}\" не найден");

                throw new FileNotFoundException("Settings file not found.");
            }

            string settingsJson = File.ReadAllText(settingsFilePath);
            JObject settings = JObject.Parse(settingsJson);

            
            baseUrl                 = (string)settings["baseUrl"];
            authFolder              = (string)settings["authFolder"];
            doorLockAllFolder       = (string)settings["doorLockAllFolder"];
            doorUnlockAllFolder     = (string)settings["doorUnlockAllFolder"];
            doorGetListFolder       = (string)settings["doorGetListFolder"];
            httpUsername            = (string)settings["httpUsername"];
            PasswordHash            = (string)settings["PasswordHash"];
            fireDoorName            = (string)settings["fireDoorName"];
            timerIntervalSeconds    = (int)settings["timerIntervalSeconds"];
            doorOpenState           = (int)settings["openState"];
            doorNormalState         = (int)settings["normalState"];

            Log.Information("Настройки из файла считаны успешно");
        }

        private async Task LoginToPortal()
        {

            var requestData = new
            {
                UserName = httpUsername,
                PasswordHash
            };
            StringContent content;
            HttpResponseMessage? response = null;
            do
            {
                Log.Information("Попытка подключения к серверу");
                try
                {
                    content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                    response = await httpClient.PostAsync(baseUrl + authFolder, content);
                    Log.Information($"Статус подключения к серверу: {response.StatusCode}");
                }
                catch (Exception)
                {
                    Log.Warning("Ошибка при подключении к серверу. Осуществляю повторную попытку подключения");
                    await Task.Delay(10000); // Задержка 10 секунд
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    await Task.Delay(10000);//Задержка 10 секунд
                }
            } while ((response == null || !response.IsSuccessStatusCode) && !cancellationTokenSource.Token.IsCancellationRequested);

            string responseBody = await response.Content.ReadAsStringAsync();
            JObject responseData = JObject.Parse(responseBody);

            JToken? userSIDToken = responseData?["UserSID"];

            if (userSIDToken != null)
            {
                Log.Information("Подключение к серверу успешно, поле userSID получено");
                userSID = (string)userSIDToken;
            }
            else
            {
                Log.Error("Ошибка получения поля userSID");
                throw new HttpRequestException($"Request failed. Recieved userSID is null");
            }
        }


        private async Task CheckFireDoorStatusPeriodically()
        {
            Log.Information("Инициализация поллинга двери FireDoor");
            int OPEN_STATE = 1;
            int NORMAL_STATE = 0;
            int NOT_IMPLEMENTED_STATE = -1;

            async Task<int> CheckFireDoorStatus()
            {

                var requestData = new
                {
                    Language = "",
                    UserSID = userSID,
                    SubscriptionEnabled = true,
                    Limit = 0,
                    StartToken = 0
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

                int fireDoorState = (int)fireDoor["HardwareState"];

                if (fireDoorState == doorOpenState)
                {
                    return OPEN_STATE;
                }
                else if (fireDoorState == doorNormalState)
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

                // Пауза между проверками
                await Task.Delay(TimeSpan.FromSeconds(timerIntervalSeconds));
            }
            Log.Information("Окончание поллинга двери FireDoor");
        }

        private async Task CallDoorUnlockAll()
        {
            Log.Information("Сработал триггер функции CallDoorUnlockAll");
            var requestData = new
            {
                Language = "",
                UserSID = userSID
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorUnlockAllFolder, content);

            Log.Information($"Функция CallDoorUnlockAll вызвана. Status code: {response.StatusCode}");
        }

        private async Task CallDoorLockAll()
        {
            Log.Information("Сработал триггер функции CallDoorLockAll");
            var requestData = new
            {
                Language = "",
                UserSID = userSID
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(baseUrl + doorLockAllFolder, content);
            Log.Information($"Функция CallDoorLockAll вызвана. Status code: {response.StatusCode}");
        }
    }
}
