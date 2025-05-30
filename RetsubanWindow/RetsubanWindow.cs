﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TakumiteAudioWrapper;
using TatehamaATS_v1.Exceptions;
using TrainCrewAPI;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Image = System.Drawing.Image;

namespace TatehamaATS_v1.RetsubanWindow
{
    enum RetsubanMode
    {
        None,
        RetsubanHead,
        RetsubanDigit,
        RetsubanTail,
        Car,
        Time,
        Unko
    }

    public partial class RetsubanWindow : Form
    {
        public string Retsuban;
        public string NewRetsuban;
        public TimeData BeforeTimeData;
        public int Car;
        public string NewCar;
        public TimeSpan ShiftTime = TimeSpan.FromHours(-10);
        public string NewHour;

        private RetsubanMode retsubanMode;

        private PictureBox[] Retsuban_7seg;
        private Dictionary<string, Image> Images_7seg;

        /// <summary>
        /// 故障発生
        /// </summary>
        internal event Action<ATSCommonException> AddExceptionAction;

        /// <summary>
        /// 設定情報変更
        /// </summary>
        internal event Action<string> SetDiaNameAction;

        private AudioManager AudioManager;
        private AudioWrapper beep1;
        private AudioWrapper beep2;
        private AudioWrapper beep3;
        private AudioWrapper set_trainsetlen;
        private AudioWrapper set_trainnum;
        private AudioWrapper set_complete;

        public RetsubanWindow()
        {
            InitializeComponent();
            this.Load += Loaded;
            var tst_time = DateTime.Now + ShiftTime;
            TopMost = true;
            BeforeTimeData = new TimeData()
            {
                hour = tst_time.Hour,
                minute = tst_time.Minute,
                second = tst_time.Second
            };
            NewHour = BeforeTimeData.hour.ToString();
            Retsuban = "";
            NewRetsuban = "";
            retsubanMode = RetsubanMode.None;
            Retsuban_7seg = new PictureBox[] { Retsuban_4, Retsuban_3, Retsuban_2, Retsuban_1 };
            Images_7seg = new Dictionary<string, Image> {
                {"0",  RetsubanResource._7seg_0} ,
                {"1",  RetsubanResource._7seg_1} ,
                {"2",  RetsubanResource._7seg_2} ,
                {"3",  RetsubanResource._7seg_3} ,
                {"4",  RetsubanResource._7seg_4} ,
                {"5",  RetsubanResource._7seg_5} ,
                {"6",  RetsubanResource._7seg_6} ,
                {"7",  RetsubanResource._7seg_7} ,
                {"8",  RetsubanResource._7seg_8} ,
                {"9",  RetsubanResource._7seg_9} ,
                {" ",  RetsubanResource._7seg_N} ,
                {"",  RetsubanResource._7seg_N}
            };
            try
            {
                AudioManager = new AudioManager();
                beep1 = AudioManager.AddAudio("sound/beep1.wav", 1.0f);
                beep2 = AudioManager.AddAudio("sound/beep2.wav", 1.0f);
                beep3 = AudioManager.AddAudio("sound/beep3.wav", 1.0f);
                set_trainnum = AudioManager.AddAudio("sound/set_trainnum.wav", 1.0f);
                set_trainsetlen = AudioManager.AddAudio("sound/set_trainsetlen.wav", 1.0f);
                set_complete = AudioManager.AddAudio("sound/set_complete.wav", 1.0f);
            }
            catch (ATSCommonException ex)
            {
                AddExceptionAction?.Invoke(ex);
            }
            catch (Exception ex)
            {
                var e = new CsharpException(9, "sound死亡", ex);
                AddExceptionAction?.Invoke(e);
            }
            set_trainnum?.PlayLoop(1.0f);
        }

        private void Loaded(object sender, EventArgs e)
        {
            RetsubanDrawing();
        }

        private void RetsubanWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            //閉じずに消す
            Hide();
            e.Cancel = true;
        }

        private void LampDrawing()
        {
            Lamp_Retsuban.Visible =
            (
                retsubanMode == RetsubanMode.RetsubanHead ||
                retsubanMode == RetsubanMode.RetsubanDigit ||
                retsubanMode == RetsubanMode.RetsubanTail
            );
            Lamp_Car.Visible = retsubanMode == RetsubanMode.Car;
            Lamp_Time.Visible = retsubanMode == RetsubanMode.Time;
        }


        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            var tst_time = DateTime.Now + ShiftTime;
            TimeData timeData = new TimeData()
            {
                hour = tst_time.Hour < 4 ? tst_time.Hour + 24 : tst_time.Hour,
                minute = tst_time.Minute,
                second = tst_time.Second
            };
            if (retsubanMode == RetsubanMode.Time)
            {
                TimeDrawing(timeData, DateTime.Now.Millisecond < 500);
                BeforeTimeData = timeData;
            }
            else
            {
                if (BeforeTimeData.second != timeData.second)
                {
                    NewHour = timeData.hour.ToString();
                    TimeDrawing(timeData);
                    BeforeTimeData = timeData;
                }
            }
            LampDrawing();
        }

        /// <summary>
        /// 列番部描画
        /// </summary>
        private void RetsubanDrawing()
        {
            // 正規表現パターンの定義                                                       
            var pattern = @"^(回|試|臨)?([0-9]{0,4})(A|B|C|K|X|Y|Z|AX|BX|CX|KX|AY|BY|CY|KY|AZ|BZ|CZ|KZ)?$";
            var match = Regex.Match(NewRetsuban, pattern);


            if (match.Success)
            {
                // Head領域 - 回・試・臨のいずれか
                string head = match.Groups[1].Value;
                // Headに画像を描画
                // 描画処理: head画像をHead領域に配置
                //先頭文字
                if (head == "回")
                {
                    Retsuban_Head.Image = RetsubanResource._16dot_Kai;
                }
                else if (head == "試")
                {
                    Retsuban_Head.Image = RetsubanResource._16dot_Shi;
                }
                else if (head == "臨")
                {
                    Retsuban_Head.Image = RetsubanResource._16dot_Rin;
                }
                else
                {
                    Retsuban_Head.Image = RetsubanResource._16dot_Null;
                }

                // 4~1領域 - 数字部分を右寄せで各桁に描画
                string digits = match.Groups[2].Value.PadLeft(4, ' '); // 数字を4桁に右寄せ、空白で埋める
                for (int i = 0; i < 4; i++)
                {
                    Retsuban_7seg[i].Image = Images_7seg[$"{digits[i]}"];
                }


                // Tail領域 - A,B,C,K,X,AX,BX,CX,KXのいずれか
                string tail = match.Groups[3].Value;
                // Tailに画像を描画
                // 描画処理: tail画像をTail領域に配置
                //接尾文字
                switch (tail)
                {
                    case "A":
                        Retsuban_Tail.Image = RetsubanResource._16dot_A;
                        break;
                    case "B":
                        Retsuban_Tail.Image = RetsubanResource._16dot_B;
                        break;
                    case "C":
                        Retsuban_Tail.Image = RetsubanResource._16dot_C;
                        break;
                    case "K":
                        Retsuban_Tail.Image = RetsubanResource._16dot_K;
                        break;
                    case "X":
                        Retsuban_Tail.Image = RetsubanResource._16dot_X;
                        break;
                    case "Y":
                        Retsuban_Tail.Image = RetsubanResource._16dot_Y;
                        break;
                    case "Z":
                        Retsuban_Tail.Image = RetsubanResource._16dot_Z;
                        break;
                    case "AX":
                        Retsuban_Tail.Image = RetsubanResource._16dot_AX;
                        break;
                    case "BX":
                        Retsuban_Tail.Image = RetsubanResource._16dot_BX;
                        break;
                    case "CX":
                        Retsuban_Tail.Image = RetsubanResource._16dot_CX;
                        break;
                    case "KX":
                        Retsuban_Tail.Image = RetsubanResource._16dot_KX;
                        break;
                    case "AY":
                        Retsuban_Tail.Image = RetsubanResource._16dot_AY;
                        break;
                    case "BY":
                        Retsuban_Tail.Image = RetsubanResource._16dot_BY;
                        break;
                    case "CY":
                        Retsuban_Tail.Image = RetsubanResource._16dot_CY;
                        break;
                    case "KY":
                        Retsuban_Tail.Image = RetsubanResource._16dot_KY;
                        break;
                    case "AZ":
                        Retsuban_Tail.Image = RetsubanResource._16dot_AZ;
                        break;
                    case "BZ":
                        Retsuban_Tail.Image = RetsubanResource._16dot_BZ;
                        break;
                    case "CZ":
                        Retsuban_Tail.Image = RetsubanResource._16dot_CZ;
                        break;
                    case "KZ":
                        Retsuban_Tail.Image = RetsubanResource._16dot_KZ;
                        break;
                    default:
                        Retsuban_Tail.Image = RetsubanResource._16dot_Null;
                        break;
                }
            }
        }

        /// <summary>
        /// 時間部描画
        /// </summary>
        /// <param name="timeData"></param>
        private void TimeDrawing(TimeData timeData, bool hourDot = false)
        {
            Time_h1.Image = hourDot ? RetsubanResource._7seg_dot : null;
            string hour = timeData.hour.ToString().PadLeft(2, ' ');
            if (retsubanMode == RetsubanMode.Time)
            {
                hour = NewHour.PadLeft(2, ' ');
            }
            Time_h2.BackgroundImage = Images_7seg[$"{hour[0]}"];
            Time_h1.BackgroundImage = Images_7seg[$"{hour[1]}"];
            string minute = timeData.minute.ToString().PadLeft(2, '0');
            Time_m2.BackgroundImage = Images_7seg[$"{minute[0]}"];
            Time_m1.BackgroundImage = Images_7seg[$"{minute[1]}"];
            string second = timeData.second.ToString().PadLeft(2, '0');
            Time_s2.BackgroundImage = Images_7seg[$"{second[0]}"];
            Time_s1.BackgroundImage = Images_7seg[$"{second[1]}"];
        }

        private void CarDrawing(int car)
        {
            CarDrawing(car.ToString());
        }

        private void CarDrawing(string car)
        {
            Debug.WriteLine($"carDraw{car}");
            car = car.PadLeft(2, ' ');
            if (car == " 0")
            {
                Car_2.BackgroundImage = Images_7seg[$" "];
                Car_1.BackgroundImage = Images_7seg[$" "];
            }
            else
            {
                Car_2.BackgroundImage = Images_7seg[$"{car[0]}"];
                Car_1.BackgroundImage = Images_7seg[$"{car[1]}"];
            }
        }

        private void Button_RetsuSet_Click_1(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：列番設定");
            Buttons_Set();
            retsubanMode = RetsubanMode.RetsubanHead;
            NewRetsuban = "";
            beep1.PlayOnce(1.0f);
            RetsubanDrawing();
        }

        private void Button_CarSet_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：両数設定");
            Buttons_Set();
            NewCar = "";
            retsubanMode = RetsubanMode.Car;
            beep1.PlayOnce(1.0f);
            CarDrawing(Car);
        }

        private void Button_TimeSet_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：時刻設定");
            Buttons_Set();
            NewHour = "";
            retsubanMode = RetsubanMode.Time;
            beep1.PlayOnce(1.0f);
            TimeDrawing(BeforeTimeData);
        }

        private void Button_UnkoSet_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：運行設定");

        }

        private void Button_VerDisplay_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：Ver表示");

        }

        private void Buttons_Set()
        {
            NewRetsuban = Retsuban;
            retsubanMode = RetsubanMode.None;
            RetsubanDrawing();
            TimeDrawing(BeforeTimeData);
            CarDrawing(Car);
        }

        private void Button_A_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：A");
            Buttons_RetsuTail("A");

        }

        private void Button_B_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：B");
            Buttons_RetsuTail("B");

        }

        private void Button_C_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：C");
            Buttons_RetsuTail("C");

        }

        private void Button_K_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：K");
            Buttons_RetsuTail("K");

        }

        private void Button_X_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：X");
            if (retsubanMode == RetsubanMode.RetsubanDigit || retsubanMode == RetsubanMode.RetsubanTail)
            {
                var pattern = @"(回|試|臨)?([0-9]{3,4})(A|B|C|K)?$";
                // 正規表現パターンの定義
                if (Regex.IsMatch(NewRetsuban, pattern))
                {
                    NewRetsuban += "X";
                    beep1.PlayOnce(1.0f);
                    RetsubanDrawing();
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
            }
        }

        private void Button_Y_Click(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("押下：Y");
            if (retsubanMode == RetsubanMode.RetsubanDigit || retsubanMode == RetsubanMode.RetsubanTail)
            {
                var pattern = @"(回|試|臨)?([0-9]{3,4})(A|B|C|K)?$";
                // 正規表現パターンの定義
                if (Regex.IsMatch(NewRetsuban, pattern))
                {
                    NewRetsuban += "Y";
                    beep1.PlayOnce(1.0f);
                    RetsubanDrawing();
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
            }
        }

        private void Button_Z_Click(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("押下：Z");
            if (retsubanMode == RetsubanMode.RetsubanDigit || retsubanMode == RetsubanMode.RetsubanTail)
            {
                var pattern = @"(回|試|臨)?([0-9]{3,4})(A|B|C|K)?$";
                // 正規表現パターンの定義
                if (Regex.IsMatch(NewRetsuban, pattern))
                {
                    NewRetsuban += "Z";
                    beep1.PlayOnce(1.0f);
                    RetsubanDrawing();
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
            }
        }

        private void Buttons_RetsuTail(string Tail)
        {
            if (retsubanMode == RetsubanMode.RetsubanDigit || retsubanMode == RetsubanMode.RetsubanTail)
            {
                var pattern = @"^(回|試|臨)?([0-9]{3,4})$";
                // 正規表現パターンの定義
                if (Regex.IsMatch(NewRetsuban, pattern))
                {
                    NewRetsuban += Tail;
                    beep1.PlayOnce(1.0f);
                    RetsubanDrawing();
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
            }
        }

        private void Button_Kai_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：回");
            Buttons_RetsuHead("回");
        }

        private void Button_Shi_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：試");
            Buttons_RetsuHead("試");
        }

        private void Button_Rin_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：臨");
            Buttons_RetsuHead("臨");
        }

        private void Buttons_RetsuHead(string Head)
        {
            if (retsubanMode == RetsubanMode.RetsubanHead)
            {
                NewRetsuban += Head;
                RetsubanDrawing();
                beep1.PlayOnce(1.0f);
                retsubanMode = RetsubanMode.RetsubanDigit;
            }
        }

        private void Button_0_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：0");
            Buttons_Digit("0");
        }

        private void Button_1_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：1");
            Buttons_Digit("1");
        }

        private void Button_2_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：2");
            Buttons_Digit("2");
        }

        private void Button_3_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：3");
            Buttons_Digit("3");
        }

        private void Button_4_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：4");
            Buttons_Digit("4");
        }

        private void Button_5_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：5");
            Buttons_Digit("5");

        }

        private void Button_6_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：6");
            Buttons_Digit("6");

        }

        private void Button_7_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：7");
            Buttons_Digit("7");

        }

        private void Button_8_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：8");
            Buttons_Digit("8");

        }

        private void Button_9_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：9");
            Buttons_Digit("9");
        }

        private void Buttons_Digit(string Digit)
        {
            if (retsubanMode == RetsubanMode.RetsubanHead || retsubanMode == RetsubanMode.RetsubanDigit)
            {
                NewRetsuban += Digit;
                beep1.PlayOnce(1.0f);
                RetsubanDrawing();

                // 正規表現パターンの定義
                var pattern = @"([回試臨]?)([0-9]{4})$";
                if (Regex.IsMatch(NewRetsuban, pattern))
                {
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
                else
                {
                    retsubanMode = RetsubanMode.RetsubanDigit;
                }
                return;
            }
            if (retsubanMode == RetsubanMode.Time)
            {
                switch (NewHour)
                {
                    case "":
                        NewHour += Digit;
                        beep1.PlayOnce(1.0f);
                        break;
                    case "0":
                    case "1":
                        NewHour += Digit;
                        beep1.PlayOnce(1.0f);
                        break;
                    case "2":
                        if (!(Digit == "8" || Digit == "9"))
                        {
                            NewHour += Digit;
                            beep1.PlayOnce(1.0f);
                        }
                        break;
                    default:
                        break;
                }
            }
            if (retsubanMode == RetsubanMode.Car)
            {
                if (NewCar == "" && (Digit != "0"))
                {
                    NewCar += Digit;
                    beep1.PlayOnce(1.0f);
                }
                else if (NewCar == "1" && Digit == "0")
                {
                    NewCar += Digit;
                    beep1.PlayOnce(1.0f);
                }
                CarDrawing(NewCar);
            }
        }

        private void Button_Set_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：設定／進");

            if (retsubanMode == RetsubanMode.RetsubanDigit || retsubanMode == RetsubanMode.RetsubanTail)
            {
                var pattern = @"^(回|試|臨)?([0-9]{3,4})(A|B|C|K|X|Y|Z|AX|BX|CX|KX|AY|BY|CY|KY|AZ|BZ|CZ|KZ)?$";
                // 正規表現パターンの定義
                if (Regex.IsMatch(NewRetsuban, pattern))
                {
                    Retsuban = NewRetsuban;
                    SetDiaNameAction?.Invoke(Retsuban);
                    retsubanMode = RetsubanMode.None;
                    RetsubanDrawing();
                    set_trainnum?.Stop();
                    if (Car != 0 || Retsuban == "9999")
                    {
                        set_trainsetlen?.Stop();
                        set_complete.PlayOnce(1.0f);
                    }
                    else
                    {
                        set_trainsetlen.PlayLoop(1.0f);
                    }
                }
                beep2.PlayOnce(1.0f);
                return;
            }
            if (retsubanMode == RetsubanMode.Time)
            {
                var newHour = Int32.Parse(NewHour);
                newHour = newHour + 24;
                ShiftTime = TimeSpan.FromHours(newHour - DateTime.Now.Hour);
                retsubanMode = RetsubanMode.None;
                beep2.PlayOnce(1.0f);
                return;
            }
            if (retsubanMode == RetsubanMode.Car)
            {
                try
                {
                    var newCar = Int32.Parse(NewCar);
                    if (2 <= newCar && newCar <= 10)
                    {
                        Car = newCar;
                    }
                    retsubanMode = RetsubanMode.None;
                    CarDrawing(NewCar);
                    set_trainsetlen?.Stop();
                    if (Retsuban != null || Retsuban == "9999")
                    {
                        set_trainnum?.Stop();
                        set_complete.PlayOnce(1.0f);
                    }
                    else
                    {
                        set_trainnum.PlayLoop(1.0f);
                    }
                    beep2.PlayOnce(1.0f);
                    return;
                }
                catch (Exception ex)
                {
                    return;
                }
            }
        }

        private void Button_Del_Click(object sender, EventArgs e)
        {
            Debug.WriteLine("押下：消去／戻");

            if (retsubanMode == RetsubanMode.RetsubanHead || retsubanMode == RetsubanMode.RetsubanDigit || retsubanMode == RetsubanMode.RetsubanTail)
            {
                if (string.IsNullOrEmpty(NewRetsuban))
                {
                    retsubanMode = RetsubanMode.RetsubanHead;
                    RetsubanDrawing();
                    return;
                }
                NewRetsuban = NewRetsuban.Remove(NewRetsuban.Length - 1);
                beep1.PlayOnce(1.0f);
                // 正規表現でモード変更
                if (Regex.IsMatch(NewRetsuban, @"([回試臨]?)([0-9]{4})(A|B|C|K)?$"))
                {
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
                else if (Regex.IsMatch(NewRetsuban, @"([回試臨]?)([0-9]{3})(A|B|C|K)$"))
                {
                    retsubanMode = RetsubanMode.RetsubanTail;
                }
                else if (Regex.IsMatch(NewRetsuban, @"([回試臨]?)([0-9]{1,4})$"))
                {
                    retsubanMode = RetsubanMode.RetsubanDigit;
                }
                else if (Regex.IsMatch(NewRetsuban, @"([回試臨])$"))
                {
                    retsubanMode = RetsubanMode.RetsubanDigit;
                }
                else if (NewRetsuban == "")
                {
                    retsubanMode = RetsubanMode.RetsubanHead;
                }
                else
                {
                    retsubanMode = RetsubanMode.None;
                }
                RetsubanDrawing();
                return;
            }
            if (retsubanMode == RetsubanMode.Time)
            {
                if (string.IsNullOrEmpty(NewHour))
                {
                    return;
                }
                NewHour = NewHour.Remove(NewHour.Length - 1);
                beep1.PlayOnce(1.0f);
                return;
            }
            if (retsubanMode == RetsubanMode.Car)
            {
                if (string.IsNullOrEmpty(NewCar))
                {
                    return;
                }
                NewCar = NewCar.Remove(NewCar.Length - 1);
                beep1.PlayOnce(1.0f);
                CarDrawing(NewCar);
                return;
            }
        }
    }
}
