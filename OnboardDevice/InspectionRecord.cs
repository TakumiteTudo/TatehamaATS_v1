using System;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using TatehamaATS_v1.Exceptions;
using TrainCrewAPI;

namespace TatehamaATS_v1.OnboardDevice
{
    /// <summary>
    /// 検査記録部
    /// </summary>
    internal class InspectionRecord
    {

        /// <summary>
        /// ログファイルパス
        /// </summary>
        private static readonly string LogFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "inspection_error.log");


        /// <summary>
        /// 故障情報マスター
        /// </summary>
        internal static Dictionary<string, ATSCommonException> exceptions = new Dictionary<string, ATSCommonException>();
        /// <summary>
        /// 故障時間
        /// </summary>
        internal static Dictionary<string, DateTime> exceptionsTime = new Dictionary<string, DateTime>();

        /// <summary>
        /// 故障発生から故障復帰までの最低時間
        /// </summary>
        private static TimeSpan resetTime = TimeSpan.FromSeconds(3.5);

        public bool RetsubanReset { get; set; }
        public bool RelayState { get; set; }
        public bool NetworkState { get; set; }
        public bool ATSReset { get; set; }
        public bool PowerReset { get; set; }
        private TimeSpan RelayUpdatedTime { get; set; }
        private TimeSpan NetworkUpdatedTime { get; set; }
        internal TrainCrewStateData TcData { get; set; }

        /// <summary>
        /// 非常状態変更
        /// </summary>
        internal event Action<bool> EmBrakeStateUpdated;

        /// <summary>
        /// 故障発生
        /// </summary>
        internal event Action<ATSCommonException> AddExceptionAction;

        /// <summary>
        /// 故障コード一覧変更
        /// </summary>
        internal event Action<List<string>> ExceptionCodesChenge;

        /// <summary>
        /// 検査記録部
        /// </summary>
        internal InspectionRecord()
        {
            exceptions.Clear();
            exceptionsTime.Clear();
            RetsubanReset = false;
            RelayState = false;
            ATSReset = false;
            PowerReset = false;
            TcData = new TrainCrewStateData();
            Task.Run(() => StartDisplayUpdateLoop());
        }

        /// <summary>
        /// 非同期で更新するループを開始する
        /// </summary>
        private async void StartDisplayUpdateLoop()
        {
            while (true)
            {
                var timer = Task.Delay(100);

                try
                {
                    //通信途絶確認
                    var NowGame = TcData.gameScreen == GameScreen.MainGame || TcData.gameScreen == GameScreen.MainGame_Pause;
                    if (DateTime.Now.TimeOfDay - RelayUpdatedTime > TimeSpan.FromSeconds(2) && NowGame)
                    {
                        var e = new RelayIOConnectionException(3, "継電部通信途絶2秒異常");
                        RelayState = false;
                        AddExceptionAction.Invoke(e);
                    }
                    if (DateTime.Now.TimeOfDay - NetworkUpdatedTime > TimeSpan.FromSeconds(5))
                    {
                        var e = new NetworkIOConnectionException(3, "地上装置通信途絶2秒異常");
                        NetworkState = false;
                        AddExceptionAction.Invoke(e);
                    }
                    //リセット条件確認
                    ResetException();
                    //故障コード一覧更新
                    ExceptionCodesChenge?.Invoke(exceptions.Values.Select(e => e.ToCode()).ToList());
                    //非常制動？
                    if (exceptions.Any(x => x.Value.ToBrake() == OutputBrake.EB))
                    {
                        EmBrakeStateUpdated?.Invoke(true);
                    }
                    else
                    {
                        EmBrakeStateUpdated?.Invoke(false);
                    }
                }
                catch (ATSCommonException ex)
                {
                    AddExceptionAction.Invoke(ex);
                }
                catch (Exception ex)
                {
                    var e = new LEDControlException(3, "", ex);
                    AddExceptionAction.Invoke(e);
                }
                finally
                {
                    await timer;
                }
            }
        }


        /// <summary>
        /// 故障追加
        /// </summary>
        /// <param name="exception"></param>
        internal void AddException(ATSCommonException exception)
        {
            EmBrakeStateUpdated?.Invoke(true);
            Debug.WriteLine($"故障追加");
            Debug.WriteLine($"{exception.Message} {exception.InnerException}");
            exceptions[exception.ToCode()] = exception;
            exceptionsTime[exception.ToCode()] = DateTime.Now;

            // ログ出力
            WriteLog(exception);
        }

        /// <summary>
        /// 故障追加
        /// </summary>
        /// <param name="exception"></param>
        internal void AddException(Exception exception)
        {
            exceptions.Add("39F", new CsharpException(3, exception.ToString()));
            exceptionsTime.Add("39F", DateTime.Now);

            // ログ出力
            WriteLog(new CsharpException(3, exception.ToString()));
        }

        /// <summary>
        /// 故障ログをファイルに追記
        /// </summary>
        /// <param name="exception"></param>
        private void WriteLog(ATSCommonException exception)
        {
            try
            {
                // 12時間以上前ならリセット
                if (File.Exists(LogFilePath))
                {
                    var lastWrite = File.GetLastWriteTime(LogFilePath);
                    if ((DateTime.Now - lastWrite).TotalHours >= 12)
                    {
                        File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
                    }
                }
                switch (exception.ToCode())
                {
                    case "397":
                    case "5CC":
                    case "7ED":
                        return;
                    default:
                        var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                                  $"Code:{exception.ToCode()} " +
                                  $"Message:{exception.Message} " +
                                  $"Inner:{exception.InnerException?.Message ?? ""}\n" +
                                  $"Stack: {exception.StackTrace}";
                        File.AppendAllText(LogFilePath, log + Environment.NewLine, Encoding.UTF8);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログ書き込み失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// リセット条件達成確認
        /// </summary>
        internal void ResetException()
        {
            var StopDetection = TcData.myTrainData.Speed == 0;
            var MasconEB = TcData.myTrainData.Bnotch == 8;
            foreach (var ex in exceptions)
            {
                string key = ex.Key;
                TimeSpan time = DateTime.Now - exceptionsTime[key];

                switch (ex.Value.ResetCondition())
                {
                    case ResetConditions.ExceptionReset:
                        if (time < resetTime)
                        {
                            //故障復帰最低時間を下回っている場合無視
                        }
                        else
                        {
                            exceptions.Remove(key);
                        }
                        break;
                    case ResetConditions.RetsubanReset:
                        if (RetsubanReset) exceptions.Remove(key);
                        break;
                    case ResetConditions.NetworkReset:
                        if (NetworkState) exceptions.Remove(key);
                        break;
                    case ResetConditions.StopDetection_RelayReset:
                        if (RelayState && StopDetection) exceptions.Remove(key);
                        break;
                    case ResetConditions.StopDetection_NetworkReset:
                        if (NetworkState && StopDetection) exceptions.Remove(key);
                        break;
                    case ResetConditions.StopDetection:
                        if (time < resetTime)
                        {
                            //故障復帰最低時間を下回っている場合無視
                        }
                        else
                        {
                            if (StopDetection) exceptions.Remove(key);
                        }
                        break;
                    case ResetConditions.StopDetection_MasconEB:
                        if (time < resetTime)
                        {
                            //故障復帰最低時間を下回っている場合無視
                        }
                        else
                        {
                            if (StopDetection && MasconEB) exceptions.Remove(key);
                        }
                        break;
                    case ResetConditions.StopDetection_MasconEB_ATSReset:
                        if (time < resetTime)
                        {
                            //故障復帰最低時間を下回っている場合無視
                        }
                        else
                        {
                            if (StopDetection && MasconEB && ATSReset) exceptions.Remove(key);
                        }
                        break;
                }
                if (PowerReset) exceptions.Remove(key);
            }
            if (StopDetection)
            {
                RelayState = false;
            }
            RetsubanReset = false;
            ATSReset = false;
            PowerReset = false;
        }

        internal void RelayUpdate(TrainCrewStateData tcData)
        {
            TcData = tcData;
            RelayUpdatedTime = DateTime.Now.TimeOfDay;
        }

        internal void NetworkUpdate()
        {
            NetworkState = true;
            NetworkUpdatedTime = DateTime.Now.TimeOfDay;
        }
    }
}
