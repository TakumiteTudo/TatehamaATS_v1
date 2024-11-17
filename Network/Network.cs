namespace TatehamaATS_v1.Network
{
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Runtime.CompilerServices;
    using Microsoft.AspNetCore.Connections;

    using Microsoft.AspNetCore.SignalR.Client;
    using TatehamaATS_v1.Exceptions;
    using static System.Windows.Forms.VisualStyles.VisualStyleElement;

    public class Network
    {
        public static HubConnection connection;
        public static async Task Connect()
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
            connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5154/hub/train")
                .WithAutomaticReconnect()
                .Build();

            connection.On<DataFromServer>("ReceiveData_ATS", DataFromServer =>
            {
                throw new NotImplementedException();
            });

            try
            {
                await connection.StartAsync();
                Console.WriteLine("Connected");
            }
            catch (Exception ex)
            {
                throw new SocketConnectException(3, "�ʐM���ڑ����ɂȂ񂩂�����", ex);
            }
            finally
            {
                await connection.StopAsync();
                await connection.DisposeAsync();
                Console.WriteLine("Cpnnection Closed");
            }
        }

        public async Task SendData_to_Server(DataToServer sendData)
        {
            await connection.SendAsync("SendData_ATS", sendData); 
        }
    }
}