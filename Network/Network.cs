namespace TatehamaATS_v1.Network
{
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Runtime.CompilerServices;
    using Microsoft.AspNetCore.SignalR.Client;
    using TatehamaATS_v1.Exceptions;
    using static System.Windows.Forms.VisualStyles.VisualStyleElement;

    public class Network
    {
        /// <summary>
        /// �̏ᔭ��
        /// </summary>
        internal event Action<ATSCommonException> AddExceptionAction;

        public Network()
        {

        }

        /// <summary>
        /// WebSocket�ڑ����s
        /// </summary>
        /// <returns></returns>
        internal async Task TryConnect()
        {
            //Todo:�ʐM�ł��ĂȂ��������ɌJ��Ԃ��悤�ɂ�����
            while (true)
            {
                try
                {
                    await Connect();
                }
                catch (ATSCommonException ex)
                {
                    AddExceptionAction.Invoke(ex);
                }
                catch (Exception ex)
                {
                    var e = new SocketException(3, "�ʐM���Ȃ񂩂�����", ex);
                    AddExceptionAction.Invoke(e);
                }
                break;
            }
        }

        private async Task Connect()
        {
            var client = new HubConnectionBuilder()
                .WithUrl("http://localhost:5154/hub/train")
                .WithAutomaticReconnect()
                .Build();
            client.On<String, String>("RecieveMessage", (user, message) =>
            {
                Console.WriteLine("Hello");
            });

            try
            {
                await client.StartAsync();
                Console.WriteLine("Connected");
                while (true)
                {
                    var message = Console.ReadLine();
                    await client.InvokeAsync("SendMessage", "arai", message);
                }
            }
            catch (Exception ex)
            {
                throw new SocketConnectException(3, "�ʐM���ڑ����ɂȂ񂩂�����", ex);
            }
            finally
            {
                await client.StopAsync();
                await client.DisposeAsync();
                Debug.WriteLine("Cpnnection Closed");
            }
        }
    }
}