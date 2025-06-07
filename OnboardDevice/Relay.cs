using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using TatehamaATS_v1.Exceptions;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using TrainCrewAPI;
using TrainCrew;
using Microsoft.AspNetCore.Routing;
using System.ComponentModel;
using System.Configuration;
using Microsoft.AspNetCore.Components.Routing;

namespace TatehamaATS_v1.OnboardDevice
{
    enum ConnectionState
    {
        /// <summary>
        /// 切断
        /// </summary>
        DisConnect,
        /// <summary>
        /// 接続中
        /// </summary>
        Connecting,
        /// <summary>
        /// 接続完了
        /// </summary>
        Connected
    }
    /// <summary>
    /// <strong>継電部</strong>
    /// TCとのWS通信を担当
    /// </summary>
    internal class Relay
    {
        // WebSocket関連のフィールド
        private ClientWebSocket _webSocket = new ClientWebSocket();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private static readonly Encoding _encoding = Encoding.UTF8;
        private readonly string _connectUri = "ws://127.0.0.1:50300/"; //TRAIN CREWのポート番号は50300

        // キャッシュ用の静的辞書
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new ConcurrentDictionary<Type, PropertyInfo[]>();
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldCache = new ConcurrentDictionary<Type, FieldInfo[]>();

        // JSONシリアライザ設定
        private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        // データ関連フィールド
        private string _command = "DataRequest";
        private string[] _request = { "tconlyontrain", "interlock" };

        // プロパティ                                                                        
        public TrainCrewStateData TcData { get; private set; } = new TrainCrewStateData();
        public RecvBeaconStateData BeaconData { get; private set; } = new RecvBeaconStateData();

        private int brake;
        private ConnectionState status = ConnectionState.DisConnect;
        private int BeforeBrake = 0;

        private List<SignalData> SignalDatas = new List<SignalData>();
        private List<Route> ServerRoutes = new List<Route>();
        private List<Route> TrainCrewRoutes = new List<Route>();
        private int RouteCounta = 0;

        private Dictionary<string, string> StaNameById = new Dictionary<string, string>()
        {
            {"TH76","館浜"},
            {"TH75","駒野"},
            {"TH71","津崎"},
            {"TH70","浜園"},
            {"TH67","新野崎"},
            {"TH66S","江ノ原信号場"},
            {"TH65","大道寺"},
            {"TH64","藤江"},
            {"TH63","水越"},
            {"TH62","高見沢"},
            {"TH61","日野森"},
            {"TH59","西赤山"},
            {"TH58","赤山町"}
        };

        /// <summary>
        /// 運転会列番
        /// </summary>
        internal string OverrideDiaName;

        // イベント
        internal event Action<TimeSpan> TC_TimeUpdated;
        internal event Action<ConnectionState> ConnectionStatusChanged;
        internal event Action<TrainCrewStateData> TrainCrewDataUpdated;
        internal event Action<RecvBeaconStateData> BeaconChenged;

        /// <summary>
        /// 故障発生
        /// </summary>
        internal event Action<ATSCommonException> AddExceptionAction;

        private int hasInvalidCharsTimes = 0;

        /// <summary>
        /// TrainCrew側データ要求コマンド
        /// (DataRequest, SetEmergencyLight, SetSignalPhase)
        /// </summary>
        public string Command
        {
            get => _command;
            set
            {
                if (value == null)
                {
                    var e = new RelayException(5, "無効なコマンドです。null");
                    AddExceptionAction.Invoke(e);
                }

                if (IsValidCommand(value))
                {
                    _command = value;
                }
                else
                {
                    var e = new RelayException(5, "無効なコマンドです。");
                    AddExceptionAction.Invoke(e);
                }
            }
        }

        /// <summary>
        /// TrainCrew側データ要求引数
        /// (all, tc, tconlyontrain, tcall, signal, train)
        /// </summary>
        public string[] Request
        {
            get => _request;
            set
            {
                if (value == null)
                {
                    var e = new RelayException(5, "無効なコマンドです。");
                    AddExceptionAction.Invoke(e);
                }

                if (IsValidRequest(_command, value))
                {
                    _request = value;
                }
                else
                {
                    var e = new RelayException(5, "無効な要求です。");
                    AddExceptionAction.Invoke(e);
                }
            }
        }

        /// <summary>
        /// コマンドの検証
        /// </summary>
        /// <param name="requestValues"></param>
        /// <returns></returns>
        private static bool IsValidCommand(string requestValues) =>
            new[] { "DataRequest", "SetEmergencyLight", "SetSignalPhase", "mode_req", "SetRoute", "DeleteRoute", "DeleteRoute2" }.Contains(requestValues);

        /// <summary>
        /// リクエストの検証
        /// </summary>
        /// <param name="commandValue"></param>
        /// <param name="requestValues"></param>
        /// <returns></returns>
        private static bool IsValidRequest(string commandValue, string[] requestValues)
        {
            switch (commandValue)
            {
                case "DataRequest":
                    return requestValues.Length == 1 && requestValues[0] == "all" ||
                           requestValues.All(str => str == "tc" || str == "tconlyontrain" || str == "tcall" || str == "signal" || str == "train");
                case "SetEmergencyLight":
                    return requestValues.Length == 2 && (requestValues[1] == "true" || requestValues[1] == "false");
                case "SetSignalPhase":
                    return requestValues.Length == 2 && (requestValues[1] == "None" || requestValues[1] == "R" || requestValues[1] == "YY" || requestValues[1] == "Y" || requestValues[1] == "YG" || requestValues[1] == "G");
                case "mode_req":
                    return requestValues.Length == 1 && (requestValues[0] == "hide_other" || requestValues[0] == "show_other" || requestValues[0] == "route_manual" || requestValues[0] == "route_auto");
                case "SetRoute":
                    return requestValues.Length == 5;
                case "DeleteRoute":
                case "DeleteRoute2":
                    return requestValues.Length == 2;
                default:
                    return false;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Relay()
        {
            OverrideDiaName = "9999";
            TrainCrewInput.Init();
            TrainCrewInput.RequestData(DataRequest.Signal);
            _webSocket = new ClientWebSocket();
        }

        /// <summary>
        /// 受信データ処理メソッド
        /// </summary>
        private void ProcessingReceiveData()
        {
            TrainCrewDataUpdated.Invoke(TcData);
            if (TcData.gameScreen == TrainCrewAPI.GameScreen.MainGame_Loading)
            {
                SetRouteMode(true);
                SetOther(true);
            }
            // TrainCrewRoutesにTcData.interlockDataListから展開した進路情報を格納する
            TrainCrewRoutes = ConvertToRoutes(TcData.interlockDataList);
        }

        /// <summary>
        /// InterlockDataのリストをRouteのリストに変換します。
        /// </summary>
        /// <param name="interlockDataList">InterlockDataのリスト</param>
        /// <returns>Routeのリスト</returns>
        public List<Route> ConvertToRoutes(List<InterlockData> interlockDataList)
        {
            var routes = new List<Route>();

            foreach (var interlockData in interlockDataList)
            {
                foreach (var interlockRoute in interlockData.routes)
                {
                    // StaNameByIdを逆向きに使用して、interlockData.Nameを駅IDにする
                    // interlockData.Nameには"連動装置"が末尾に含まれているため、削除してから検索する
                    var stationId = StaNameById.FirstOrDefault(x => x.Value == interlockData.Name.Replace("連動装置", "")).Key;
                    var route = new Route
                    {

                        TcName = $"{stationId}_{interlockRoute.Name}",
                        RouteType = RouteType.SwitchRoute,
                        Indicator = "",
                        RouteState = null
                    };

                    routes.Add(route);
                }
            }

            return routes;
        }

        /// <summary>
        /// WebSocket接続試行
        /// </summary>
        /// <returns></returns>
        internal async Task TryConnectWebSocket()
        {
            status = ConnectionState.DisConnect;
            ConnectionStatusChanged?.Invoke(status);
            while (true)
            {
                _webSocket = new ClientWebSocket();

                try
                {
                    status = ConnectionState.Connecting;
                    ConnectionStatusChanged?.Invoke(status);
                    // 接続処理
                    await ConnectWebSocket();
                    break;
                }
                catch (ATSCommonException ex)
                {
                    AddExceptionAction.Invoke(ex);
                }
                catch (Exception ex)
                {
                    status = ConnectionState.DisConnect;
                    ConnectionStatusChanged?.Invoke(status);
                    var e = new RelayConnectException(5, "", ex);
                    AddExceptionAction.Invoke(e);
                    await Task.Delay(1000);
                }
            }
        }

        /// <summary>
        /// WebSocket接続処理
        /// </summary>
        /// <returns></returns>
        private async Task ConnectWebSocket()
        {
            // 送信処理
            await SendMessages();
            // 受信処理
            await ReceiveMessages();
        }

        /// <summary>
        /// WebSocket送信処理
        /// </summary>
        private async Task SendMessages()
        {
            try
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    await _webSocket.ConnectAsync(new Uri(_connectUri), CancellationToken.None);
                    status = ConnectionState.Connected;
                    ConnectionStatusChanged?.Invoke(status);
                }

                CommandToTrainCrew requestCommand = new CommandToTrainCrew()
                {
                    command = _command,
                    args = _request
                };

                // JSON形式にシリアライズ
                string json = JsonConvert.SerializeObject(requestCommand, JsonSerializerSettings);
                byte[] bytes = _encoding.GetBytes(json);

                // WebSocket送信
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                status = ConnectionState.DisConnect;
                ConnectionStatusChanged?.Invoke(status);
                throw new RelayFirstConnectException(5, "50300弾かれ", ex);
            }
        }

        private async Task SendMessages(string command, string[] request)
        {
            try
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    await _webSocket.ConnectAsync(new Uri(_connectUri), CancellationToken.None);
                    status = ConnectionState.Connected;
                    ConnectionStatusChanged?.Invoke(status);
                }

                CommandToTrainCrew requestCommand = new CommandToTrainCrew()
                {
                    command = command,
                    args = request
                };

                // JSON形式にシリアライズ
                string json = JsonConvert.SerializeObject(requestCommand, JsonSerializerSettings);
                byte[] bytes = _encoding.GetBytes(json);

                // WebSocket送信
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                status = ConnectionState.DisConnect;
                ConnectionStatusChanged?.Invoke(status);
                throw new RelayConnectException(5, "指示コマンド送信失敗", ex);
            }
        }

        internal void SignalSet(List<SignalData> signalDatas)
        {
            foreach (var signalData in signalDatas)
            {
                _ = SendSingleCommand("SetSignalPhase", new string[] { signalData.Name, signalData.phase.ToString() });
            }
            //情報の送られてこなくなった信号機の現示をNoneにする
            var outSignals = SignalDatas
                .Where(s2 => !signalDatas.Any(s1 => s1.Name == s2.Name))
                .ToList();
            foreach (var signalData in outSignals)
            {
                _ = SendSingleCommand("SetSignalPhase", new string[] { signalData.Name, Phase.None.ToString() });
            }
            SignalDatas = signalDatas;
        }

        internal void EMSet(List<EmergencyLightData> emergencyLightDatas)
        {
            foreach (var emergencyLightData in emergencyLightDatas)
            {
                SendSingleCommand("SetEmergencyLight", new string[] { emergencyLightData.Name, emergencyLightData.State ? "true" : "false" });
            }
        }

        internal void SetOther(bool isHide)
        {
            SendSingleCommand("mode_req", new string[] { isHide ? "hide_other" : "show_other" });
        }

        internal void SetRouteMode(bool isManual)
        {
            SendSingleCommand("mode_req", new string[] { isManual ? "route_manual" : "route_auto" });
        }

        internal void UpdateRoute(List<Route> routes)
        {
            // routes または Routes が null の場合は処理をスキップ
            if (routes == null)
            {
                Debug.WriteLine("routes is null. Skipping UpdateRoute.");
                return;
            }

            if (TrainCrewRoutes == null)
            {
                Debug.WriteLine("ServerRoutes is null. Initializing empty list.");
                TrainCrewRoutes = new List<Route>();
            }

            // TrainCrewRoutesの設定内容を項目ごとにDebug出力
            Debug.WriteLine("TrainCrewRoutes:");
            foreach (var route in TrainCrewRoutes)
            {
                Debug.WriteLine($"  TcName: {route.TcName}");
            }


            // 保存した差分と比較し、増えた分はSetRoute、減った分はDeleteRouteを実行する
            var addedRoutes = routes.Where(r => !TrainCrewRoutes.Any(r2 => r2.TcName == System.Text.RegularExpressions.Regex.Replace(r.TcName, @"[ST]([A-Z])$", "$1"))).ToList();
            var removedRoutes = TrainCrewRoutes.Where(r => !routes.Any(r2 => System.Text.RegularExpressions.Regex.Replace(r2.TcName, @"[ST]([A-Z])$", "$1") == r.TcName)).ToList();

            foreach (var route in addedRoutes)
            {
                SetRoute(route);
            }
            foreach (var route in removedRoutes)
            {
                DeleteRoute(route);
            }
        }

        private void SetRoute(Route route)
        {
            try
            {
                var r = route.TcName.Split('_').ToList();
                // staID仮対応
                var staName = StaNameById[r[0]] + "連動装置";

                // 末尾が "S[A-Z]" または "T[A-Z]" の場合に "[A-Z]" の部分だけを残す
                var routeName = System.Text.RegularExpressions.Regex.Replace(r[1], @"[ST]([A-Z])$", "$1");

                // Todo: 出発の場合は、列選表示とする。
                string indicator;
                switch (route.RouteType)
                {
                    case RouteType.Arriving:
                    case RouteType.SwitchSignal:
                    case RouteType.SwitchRoute:
                    case RouteType.Guide:
                        indicator = route.Indicator;
                        break;
                    case RouteType.Departure:
                        indicator = TypeString(OverrideDiaName);
                        break;
                    default:
                        indicator = "";
                        break;
                }
                Debug.WriteLine($"☆API送信: SetRoute/{route.TcName}");
                SendSingleCommand("SetRoute", [staName, routeName, indicator, TcData.myTrainData.diaName, "停車"]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ex.Message}{ex.InnerException}");
            }
        }

        private void DeleteRoute(Route route)
        {
            try
            {
                var r = route.TcName.Split('_').ToList();
                // staID仮対応
                var staName = StaNameById[r[0]] + "連動装置";

                // 末尾が "S[A-Z]" または "T[A-Z]" の場合に "[A-Z]" の部分だけを残す
                var routeName = System.Text.RegularExpressions.Regex.Replace(r[1], @"[ST]([A-Z])$", "$1");

                Debug.WriteLine($"☆API送信: DeleteRoute2/{route.TcName}");
                SendSingleCommand("DeleteRoute2", [staName, routeName]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ex.Message}{ex.InnerException}");
            }
        }


        internal string TypeString(string Retsuban)
        {
            Retsuban = Retsuban.Replace("X", "").Replace("Y", "").Replace("Z", "");
            if (Retsuban == "9999")
            {
                return "";
            }
            if (Retsuban.Contains("溝月"))
            {
                return "回送";
            }
            if (Retsuban.StartsWith("回"))
            {
                return "回送";
            }
            if (Retsuban.StartsWith("試"))
            {
                return "回送";
            }

            if (Retsuban.Contains("A"))
            {
                return "A特";
            }
            if (Retsuban.Contains("K"))
            {
                return "快速急行";
            }
            if (Retsuban.Contains("B"))
            {
                return "急行";
            }
            if (Retsuban.Contains("C"))
            {
                return "準急";
            }
            if (Retsuban.StartsWith("臨"))
            {
                return "回送";
            }
            if (int.TryParse(Retsuban, null, out _))
            {
                return "普通";
            }
            return "回送";
        }

        internal async Task SendSingleCommand(string command, string[] request)
        {
            try
            {
                // コマンドとリクエストを検証
                if (IsValidCommand(command) && IsValidRequest(command, request))
                {
                    await SendMessages(command, request);
                }
                else
                {
                    throw new RelayException(5, $"無効なコマンド({command})または要求{string.Join(",", request)}です。");
                }
            }
            catch (ATSCommonException ex)
            {
                AddExceptionAction.Invoke(ex);
            }
            catch (Exception ex)
            {
                status = ConnectionState.DisConnect;
                ConnectionStatusChanged?.Invoke(status);
                var e = new RelayConnectException(5, "指示コマンド送信失敗", ex);
                AddExceptionAction.Invoke(e);
                await Task.Delay(1000);
            }
        }

        /// <summary>
        /// WebSocket受信処理
        /// </summary>
        /// <returns></returns>
        private async Task ReceiveMessages()
        {
            var buffer = new byte[2048];
            var messageBuilder = new StringBuilder();

            while (_webSocket.State == WebSocketState.Open)
            {
                List<byte> messageBytes = new List<byte>();
                _stopwatch.Restart();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // サーバーからの切断要求を受けた場合
                        await CloseAsync();
                        status = ConnectionState.DisConnect;
                        ConnectionStatusChanged?.Invoke(status);
                        await TryConnectWebSocket();
                        return;
                    }
                    else
                    {
                        messageBytes.AddRange(buffer.Take(result.Count));
                    }

                } while (!result.EndOfMessage);

                // データが揃ったら文字列へエンコード
                string jsonResponse = _encoding.GetString(messageBytes.ToArray());
                messageBytes.Clear();

                // 文字化けチェック
                if (HasInvalidChars(jsonResponse))
                {
                    Debug.WriteLine("☆文字化け元データ");
                    Debug.WriteLine(jsonResponse);
                    hasInvalidCharsTimes++;
                    if (hasInvalidCharsTimes > 20)
                    {
                        var e = new RelayOtherInfoAbnormal(5, $"文字化け検知${hasInvalidCharsTimes}回目");
                        AddExceptionAction.Invoke(e);
                    }
                    continue;
                }
                else
                {
                    hasInvalidCharsTimes = 0;
                }

                // 一旦Data_Base型でデシリアライズ
                var baseData = JsonConvert.DeserializeObject<Data_Base>(jsonResponse, JsonSerializerSettings);

                if (baseData != null)
                {
                    // Typeプロパティに応じて処理
                    if (baseData.type == "TrainCrewStateData")
                    {
                        // Debug.WriteLine(baseData.data);
                        // Data_Base.DataをTrainCrewStateData型にデシリアライズ
                        var _trainCrewStateData = JsonConvert.DeserializeObject<TrainCrewStateData>(baseData.data.ToString());

                        if (_trainCrewStateData != null)
                        {
                            // JSON受信データ処理
                            lock (TcData)
                            {
                                UpdateFieldsAndProperties(TcData, _trainCrewStateData);
                                // Form関連処理                           
                                TC_TimeUpdated?.Invoke(TcData.nowTime.ToTimeSpan());
                            }
                            // その他処理
                            ProcessingReceiveData();
                        }
                        else
                        {
                            var e = new RelayCarInfoAbnormal(5, "TcData作成失敗");
                            AddExceptionAction.Invoke(e);
                        }
                    }
                    else if (baseData.type == "RecvBeaconStateData")
                    {
                        // Data_Base.DataをRecvBeaconStateData型にデシリアライズ
                        var _recvBeaconStateData = JsonConvert.DeserializeObject<RecvBeaconStateData>(baseData.data.ToString());

                        if (_recvBeaconStateData != null)
                        {
                            // JSON受信データ処理
                            lock (BeaconData)
                            {
                                UpdateFieldsAndProperties(BeaconData, _recvBeaconStateData);
                                // Form関連処理
                                BeaconChenged?.Invoke(BeaconData);
                            }
                        }
                        else
                        {
                            var e = new TransponderInfoAbnormal(5, "BeaconData作成失敗");
                            AddExceptionAction.Invoke(e);
                        }
                    }
                    else if (baseData.type == "APIMessage")
                    {
                        // Data_Base.DataをAPIMessage型にデシリアライズ
                        var _APIMessage = JsonConvert.DeserializeObject<APIMessage>(baseData.data.ToString());
                        Debug.WriteLine($"☆API応答：{_APIMessage.title}：{_APIMessage.message}");
                    }
                    else
                    {
                        var e = new RelayOtherInfoAbnormal(5, "不明タイプ");
                        AddExceptionAction.Invoke(e);
                    }
                }
                _stopwatch.Stop();
                string s = (_stopwatch.Elapsed.TotalSeconds * 1000).ToString("F2");
            }
        }

        /// <summary>
        /// WebSocket終了処理
        /// </summary>
        /// <returns></returns>
        private async Task CloseAsync()
        {
            SetOther(false);
            SetRouteMode(false);
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                // 正常に接続を閉じる
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                status = ConnectionState.DisConnect;
                ConnectionStatusChanged?.Invoke(status);
            }
            _webSocket.Dispose();
        }

        /// <summary>
        /// 文字列に不正な文字が含まれているか判定する
        /// </summary>
        public static bool HasInvalidChars(string input)
        {
            foreach (char c in input)
            {
                // 制御文字（改行・タブを除く）またはU+FFFD（�）が含まれていたら文字化け
                if ((char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') || c == '\uFFFD')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// フィールド・プロパティ置換メソッド
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="source"></param>
        /// <exception cref="ArgumentNullException"></exception>
        private void UpdateFieldsAndProperties<T>(T target, T source) where T : class
        {
            if (target == null || source == null)
            {
                var e = new RelayException(5, "ターゲットまたはソースは null にできません");
                AddExceptionAction.Invoke(e);
            }

            // プロパティのキャッシュを取得または設定
            var properties = PropertyCache.GetOrAdd(target.GetType(), t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            foreach (var property in properties)
            {
                if (property.CanWrite)
                {
                    var newValue = property.GetValue(source);
                    property.SetValue(target, newValue);
                }
            }

            // フィールドのキャッシュを取得または設定
            var fields = FieldCache.GetOrAdd(target.GetType(), t => t.GetFields(BindingFlags.Public | BindingFlags.Instance));
            foreach (var field in fields)
            {
                var newValue = field.GetValue(source);
                field.SetValue(target, newValue);
            }
        }

        public void SetEB(bool State, bool force = false)
        {
            if (State)
            {
                TrainCrewInput.SetATO_Notch(-8);
                brake = -8;
            }
            else if ((brake == -8 || force) && !State)
            {
                TrainCrewInput.SetATO_Notch(0);
                brake = 0;
            }
        }

        public void ATSResetPush()
        {
            //Todo:ATS復帰入力
        }

        public void ForceStopSignal(bool IsStop)
        {
            TrainCrewInput.GetTrainState();
            foreach (var signalData in TrainCrewInput.signals)
            {
                if (IsStop)
                {
                    _ = SendSingleCommand("SetSignalPhase", new string[] { signalData.name, Phase.R.ToString() });
                }
                else
                {
                    _ = SendSingleCommand("SetSignalPhase", new string[] { signalData.name, Phase.None.ToString() });
                }
            }
        }
    }
}
