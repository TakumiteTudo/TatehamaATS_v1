﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using TatehamaATS_v1.ATSDisplay;
using TatehamaATS_v1.Exceptions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace TatehamaATS_v1.KokuchiWindow
{
    public partial class KokuchiWindow : Form
    {
        OperationNotificationData KokuchiData;
        private Bitmap sourceImage;
        public bool ShowLED;


        /// <summary>
        /// 故障発生
        /// </summary>
        internal event Action<ATSCommonException> AddExceptionAction;

        public KokuchiWindow()
        {
            InitializeComponent();
            sourceImage = KokuchiResource.Kokuchi_LED;
            DisplayImageByPos(1, 154);
            TopMost = true;
            BackColor = Color.Blue;
            TransparencyKey = BackColor;
            ShowLED = false;
        }

        private void KokuchiWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            //閉じずに消す
            Hide();
            e.Cancel = true;
        }

        public void SetData(OperationNotificationData? kokuchiData)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<OperationNotificationData?>(SetData), kokuchiData);
            }
            else
            {
                if (kokuchiData != null)
                {
                    Transparency.Visible = false;
                    KokuchiData = kokuchiData;
                }
                else
                {
                    Transparency.Visible = true;
                }
            }
        }

        /// <summary>
        /// 表示内容を変更する
        /// </summary>
        private void SetLED(OperationNotificationData kokuchiData)
        {
            try
            {
                switch (kokuchiData.Type)
                {
                    case OperationNotificationType.None:
                        DisplayImageByPos(1, 1);
                        break;
                    case OperationNotificationType.Yokushi:
                        DisplayImageByPos(1, 18);
                        break;
                    case OperationNotificationType.Tsuuchi:
                    case OperationNotificationType.TsuuchiKaijo:
                        DisplayImageByPos(1, 35);
                        break;
                    case OperationNotificationType.Shuppatsu:
                        DisplayImageByPos(1, 52);
                        break;
                    case OperationNotificationType.Kaijo:
                        DisplayImageByPos(1, 69);
                        break;
                    case OperationNotificationType.Tenmatsusho:
                        if (kokuchiData.Content == "MC")
                        {
                            DisplayImageByPos(1, 86);
                        }
                        else if (kokuchiData.Content == "M")
                        {
                            DisplayImageByPos(1, 103);
                        }
                        else if (kokuchiData.Content == "C")
                        {
                            DisplayImageByPos(1, 120);
                        }
                        break;
                    case OperationNotificationType.ShuppatsuJikoku:
                        DisplayTimeImage(kokuchiData.Content);
                        break;
                    case OperationNotificationType.Torikeshi:
                        DisplayImageByPos(1, 137);
                        break;
                    default:
                        DisplayImageByPos(1, 171);
                        break;
                }
            }
            catch (ATSCommonException ex)
            {
                DisplayImageByPos(1, 171);
            }
        }

        /// <summary>
        /// 座標指定で画像出す
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void DisplayImageByPos(int x, int y, int width = 48, int height = 16)
        {
            var Image = GetImageByPos(x, y, width, height);
            var BigImage = EnlargePixelArt(Image);
            KokuchiLED.BackgroundImage = BigImage;
        }


        private void DisplayTimeImage(string Time)
        {
            if (int.TryParse(Time, out int result))
            {
                var De = GetImageByPos(50, 1);
                var M2 = GetImageByPos(50, 1 + 17 * int.Parse(Time[0].ToString()), 9);
                var M1 = GetImageByPos(59, 1 + 17 * int.Parse(Time[1].ToString()), 9);
                var S2 = GetImageByPos(68, 1 + 17 * int.Parse(Time[2].ToString()), 9);
                var S1 = GetImageByPos(74, 1 + 17 * int.Parse(Time[3].ToString()), 9);

                using (Graphics g = Graphics.FromImage(De))
                {
                    g.DrawImage(M2, 0, 0, M2.Width, M2.Height);
                    g.DrawImage(M1, 9, 0, M1.Width, M1.Height);
                    g.DrawImage(S2, 18, 0, S2.Width, S2.Height);
                    g.DrawImage(S1, 24, 0, S1.Width, S1.Height);
                }
                var BigImage = EnlargePixelArt(De);
                KokuchiLED.BackgroundImage = BigImage;
            }
            else
            {
                DisplayImageByPos(1, 171);
            }
        }

        /// <summary>
        /// 指定した座標とサイズに基づいて画像を切り出す
        /// </summary>
        /// <param name="number">切り出す画像の番号</param>
        /// <returns>切り出された画像</returns>
        private Bitmap GetImageByPos(int x, int y, int width = 48, int height = 16)
        {
            Bitmap croppedImage = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(croppedImage))
            {
                g.DrawImage(sourceImage, new Rectangle(0, 0, width, height), new Rectangle(x, y, width, height), GraphicsUnit.Pixel);
            }

            return croppedImage;
        }

        /// <summary>
        /// ピクセルアートを6倍に拡大する
        /// </summary>
        /// <param name="original">元の画像</param>
        /// <returns>6倍に拡大された画像</returns>
        private Bitmap EnlargePixelArt(Bitmap original)
        {
            int newWidth = original.Width * 6;
            int newHeight = original.Height * 6;

            Bitmap enlargedImage = new Bitmap(newWidth + 1, newHeight + 1);
            using (Graphics g = Graphics.FromImage(enlargedImage))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        Color pixelColor = original.GetPixel(x, y);
                        for (int dy = 0; dy < 6; dy++)
                        {
                            for (int dx = 0; dx < 6; dx++)
                            {
                                enlargedImage.SetPixel(x * 6 + dx, y * 6 + dy, pixelColor);
                            }
                        }
                    }
                }
            }

            return enlargedImage;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (ShowLED)
            {
                if (this.BackgroundImage != null)
                {
                    this.BackgroundImage = null;
                    KokuchiLED.BackgroundImage = null;
                    KokuchiLED.Image = null;
                }
            }
            else
            {
                if (this.BackgroundImage == null)
                {
                    this.BackgroundImage = KokuchiResource.Kokuchi_Background;
                    KokuchiLED.Image = KokuchiResource.KokuchiLED_Waku;
                }
                if (KokuchiData == null)
                {
                    DisplayImageByPos(1, 154);
                    return;
                }
                var DeltaTime = (DateTime.Now - KokuchiData.OperatedAt).TotalMilliseconds;

                switch (KokuchiData.Type)
                {
                    case OperationNotificationType.None:
                    case OperationNotificationType.Kaijo:
                    case OperationNotificationType.Shuppatsu:
                    case OperationNotificationType.ShuppatsuJikoku:
                    case OperationNotificationType.Torikeshi:
                        //点滅しないやつ
                        SetLED(KokuchiData);
                        break;
                    case OperationNotificationType.Yokushi:
                    case OperationNotificationType.Tsuuchi:
                        //1000+500点滅
                        if (DeltaTime % 1500 < 1000)
                        {
                            SetLED(KokuchiData);
                        }
                        else
                        {
                            DisplayImageByPos(50, 171);
                        }
                        break;
                    case OperationNotificationType.TsuuchiKaijo:
                        if (DeltaTime < 5 * 1000)
                        {
                            //500+250点滅     
                            if (DeltaTime % 750 < 500)
                            {
                                SetLED(KokuchiData);
                            }
                            else
                            {
                                DisplayImageByPos(50, 171);
                            }
                        }
                        else if (DeltaTime < 20 * 1000)
                        {
                            //250+250点滅     
                            if (DeltaTime % 500 < 250)
                            {
                                SetLED(KokuchiData);
                            }
                            else
                            {
                                DisplayImageByPos(50, 171);
                            }
                        }
                        else
                        {
                            DisplayImageByPos(50, 171);
                        }
                        break;
                    case OperationNotificationType.Tenmatsusho:
                        if (DeltaTime % 2000 < 1500)
                        {
                            //1500+500点滅   
                            SetLED(KokuchiData);
                        }
                        else
                        {
                            DisplayImageByPos(50, 171);
                        }
                        break;
                    default:
                        DisplayImageByPos(50, 171);
                        break;
                }
            }
        }

        private void KokuchiLED_Click(object sender, EventArgs e)
        {
        }
    }
}
