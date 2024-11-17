﻿using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using TatehamaATS_v1.Exceptions;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

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
        private string[] _request = { "all" };
        //all仮設定　必要な分に後で絞る  tconlyontrain?                   

        // プロパティ                                                                        
        internal DataFromTrainCrew TcData { get; private set; } = new DataFromTrainCrew();

        private ConnectionState status = ConnectionState.DisConnect;
        private int BeforeBrake = 0;

        // イベント
        internal event Action<TimeSpan> TC_TimeUpdated;
        internal event Action<ConnectionState> ConnectionStatusChanged;
        internal event Action<DataFromTrainCrew> TrainCrewDataUpdated;

        /// <summary>
        /// 故障発生
        /// </summary>
        internal event Action<ATSCommonException> AddExceptionAction;


        /// <summary>
        /// TrainCrew側データ要求コマンド
        /// (DataRequest, SetEmergencyLight, SetSignalPhase)
        /// </summary>
        internal string Command
        {
            get => _command;
            set
            {
                if (value == null)
                {
                    var e = new RelayException(3, "無効なコマンドです。null");
                    AddExceptionAction.Invoke(e);
                }

                if (IsValidCommand(value))
                {
                    _command = value;
                }
                else
                {
                    var e = new RelayException(3, "無効なコマンドです。");
                    AddExceptionAction.Invoke(e);
                }
            }
        }

        /// <summary>
        /// TrainCrew側データ要求引数
        /// (all, tc, tconlyontrain, tcall, signal, train)
        /// </summary>
        internal string[] Request
        {
            get => _request;
            set
            {
                if (value == null)
                {
                    var e = new RelayException(3, "無効なコマンドです。");
                    AddExceptionAction.Invoke(e);
                }

                if (IsValidRequest(_command, value))
                {
                    _request = value;
                }
                else
                {
                    var e = new RelayException(3, "無効な要求です。");
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
            new[] { "DataRequest", "SetEmergencyLight", "SetSignalPhase" }.Contains(requestValues);

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
                default:
                    return false;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Relay()
        {
            _webSocket = new ClientWebSocket();
        }

        /// <summary>
        /// 受信データ処理メソッド
        /// </summary>
        private void ProcessingReceiveData()
        {
            TrainCrewDataUpdated.Invoke(TcData);
            // 必要情報の抜き出し    
            var nowOnTrack = new List<string>();
            foreach (var circuit in TcData.trackCircuitList)
            {
                if (circuit.Last == TcData.myTrainData.diaName)
                {
                    nowOnTrack.Add(circuit.Name);
                }
            }
            //Debug.WriteLine($"在線トラック：{string.Join(",", nowOnTrack)}");
        }

        /// <summary>
        /// WebSocket接続試行
        /// </summary>
        /// <returns></returns>
        internal async Task TryConnectWebSocket()
        {
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
                    var e = new RelayConnectException(3, "", ex);
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
                    command = Command,
                    args = Request
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
                throw new RelayConnectException(3, "50300弾かれ", ex);
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
                throw new RelayConnectException(3, "50300弾かれ", ex);
            }
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
                    throw new RelayException(3, $"無効なコマンド({command})または要求{string.Join(",", request)}です。");
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
                var e = new RelayConnectException(3, "", ex);
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
                        string partMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuilder.Append(partMessage);
                    }

                } while (!result.EndOfMessage);

                string jsonResponse = messageBuilder.ToString();
                messageBuilder.Clear();

                // JSON受信データ処理
                lock (TcData)
                {
                    var newData = JsonConvert.DeserializeObject<DataFromTrainCrew>(jsonResponse);
                    if (newData != null)
                    {
                        UpdateFieldsAndProperties(TcData, newData);
                    }
                    TC_TimeUpdated?.Invoke(TcData.nowTime.ToTimeSpan());

                    // その他処理
                    ProcessingReceiveData();
                }
            }
        }

        /// <summary>
        /// WebSocket終了処理
        /// </summary>
        /// <returns></returns>
        private async Task CloseAsync()
        {
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
                var e = new RelayException(3, "ターゲットまたはソースは null にできません");
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
    }
}