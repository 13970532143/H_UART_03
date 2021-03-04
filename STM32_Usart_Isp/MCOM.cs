
using File_HexToBin.HexAndBin;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

namespace STM32_Usart_Isp
{
    class MCOM
    {
        public SerialPort mySerail = new SerialPort();
        Stm32UsartIsp Stm32UsartIsp = new Stm32UsartIsp();
        public MCOM()
        {
            Debug.WriteLine("创建串口类\r\n");
            //mySerail = new SerialPort();
            //mySerail.DataReceived += new SerialDataReceivedEventHandler(port_DataReceived); 
        }

        //接收到数据事件
        public event Action<object, string> ShowDataReceived;

        private void OnShowDataReceived(string str)
        {
            if (ShowDataReceived != null)
            {
                ShowDataReceived(this,str);
            }
        }
        private void SerialPort1_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        //private void port_DataReceived(object sender, EventArgs e)
        {
            Byte[] DataBuff = new byte[1024];
            if (mySerail.IsOpen == false)  //如果断开了  直接退出
            {
                return ;
            }
            try
            {

                int SerCount = mySerail.BytesToRead;  //读取串口缓存中数据大小
                mySerail.Read(DataBuff, 0, SerCount);
                Debug.WriteLine("接收数据长度{0}：\r\n" + Stm32UsartIsp.ToHexString(DataBuff), DataBuff.Length);
                OnShowDataReceived(DataBuff.ToString());

            }
            catch 
            {
                MessageBox.Show("接收错误");
            }
        }
        //txMsg:要发送的字符串
        //txMod：false - 字符串格式  true-HEX 格式
        public bool Send_Mes(string txMsg,bool txMod)
        {
            mySerail.Write(txMsg);
            Console.Write("发送："+txMsg);
            //if (!txMod)
            //{
            //    mySerail.Write(txMsg);
            //}
            return true;
        }

    }
}
