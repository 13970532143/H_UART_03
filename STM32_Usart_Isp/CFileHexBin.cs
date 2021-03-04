using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace File_HexToBin.HexAndBin
{
    class CFileHexBin
    {
        private string _UpdataFilePath;
        /// <summary>
        /// 当前装载的升级文件路径
        /// </summary>
        public string UpdataFilePath { get { return _UpdataFilePath; } }

        private int _UpdataFileTyp = -1;
        /// <summary>
        /// 返回当前装载的文件格式  -1未装载  1 IHEX HEX   2 BIN
        /// </summary>
        public int UpdataFileTyp { get { return _UpdataFileTyp; } }




        private List<Byte> Listbuff = new List<byte>();
        public List<Byte> DataList { get { return Listbuff; } }
        public UInt32 FlashStaraddr { get; set; }

        /// <summary>
        /// 判断十六进制字符串hex是否正确
        /// </summary>
        /// <param name="hex">十六进制字符串</param>
        /// <returns>true：不正确，false：正确</returns>
        public bool IsIllegalHexadecimal(string hex)
        {
            const string PATTERN = @"([^A-Fa-f0-9]|\s+?)+";
            return Regex.IsMatch(hex, PATTERN);

        }

        ///// <summary>
        ///// 判断十六进制字符串hex是否正确
        ///// </summary>
        ///// <param name="hex">十六进制字符串</param>
        ///// <returns>true：不正确，false：正确</returns>
        //public bool IsIllegalHexadecimal(string hex)
        //{
        //    IList<char> HexSet = new List<char>() { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F', 'a', 'b', 'c', 'd', 'e', 'f' };
        //    foreach (char item in hex)
        //    {
        //        if (!HexSet.Contains<char>(item))
        //            return true;
        //    }
        //    return false;
        //}

        /// <summary>
        /// 是否执行重装载  真 重装
        /// </summary>
        public bool ResetLoadingUpdataFile
        {
            set
            {
                if (GetFileListDataLoading() == false)
                {
                    MessageBox.Show("未载入升级文件");
                    return;
                }
                if (value == true)
                {
                    Loading_OpenUpdataFile(UpdataFilePath);
                }
            }
        }


        /// <summary>
        /// 返回文件大小  如果文件路径是错误的  则提示警告 返回-1
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static long GetFileSize(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                return info.Length;
            }
            catch (IOException e)
            {
                MessageBox.Show(e.Message);
            }
            return -1;
        }
        /// <summary>
        /// 检查当前文件List数据是否为空 即是否装载文件
        /// </summary>
        /// <returns>真 已经装载文件   </returns>
        public bool GetFileListDataLoading()
        {
            return (UpdataFileTyp != -1);
        }
        /// <summary>
        /// byte 数组 累加
        /// </summary>
        /// <param name="Data"></param>
        /// <returns></returns>
        public static byte ByteAddTable(byte[] Data)
        {
            byte Val = 0;
            foreach (var item in Data)
            {
                Val += item;
            }
            return Val;
        }

        /// <summary>
        /// 检测一行HEX文件数据是否正常 并解析成字节数组
        /// </summary>
        /// <param name="filedata">HEX文件一行数据</param>
        /// <param name="ByteData">解析后的字节数组 函数内部赋值数组长度</param>
        /// <returns>返回0 正确  其它为错误信息</returns>
        private int TextOneHexData(byte[] filedata, ref byte[] ByteData)
        {
            if ((filedata[0] != ':') || ((filedata.Length - 1) % 2) > 0) //第一个字符必须为： 且长度减一必须为偶数
            {
                return 1;
            }
            string str = System.Text.Encoding.Default.GetString(filedata, 1, filedata.Length - 1);
            if (IsIllegalHexadecimal(str) == true)  //判断是否为十六进制字符  
            {
                return 2;
            }

            int Count = str.Length / 2;
            ByteData = new byte[str.Length / 2];  //数据保存
            for (int i = 0; i < Count; i++)
            {
                ByteData[i] = Convert.ToByte(str.Substring(i * 2, 2), 16);   //把字符串转换成十六进制
            }
            //检验是否为HEX文件格式-----------------------
            if ((ByteData[0] + 5) != ByteData.Length)  //长度错误
            {
                return 3;
            }
            if (ByteData[3] > 5)  //记录类型错误
            {
                return 4;
            }
            //检查CRC检验-------------------------
            byte[] DataBf = new byte[ByteData.Length - 1];
            Array.Copy(ByteData, DataBf, DataBf.Length);  //拷贝前面的数据  最后一个字节数据为CRC 不拷贝
            byte CrcData = ByteAddTable(DataBf);    //求和
            CrcData = (byte)(~CrcData + 1);  //取反 + 1
            if (((byte)CrcData) != ByteData[Count - 1])  //CRC 错误
            {
                return 5;
            }
            return 0;
        }


        /// <summary>
        /// 装载升级文件  支持HEX IHEX  BIN
        /// </summary>
        /// <param name="filepath">升级文件的全路径</param>
        /// <param name="filetyp">载入的文件类型 -1 不支持的格式  1 .HEX .IHEX  2 .Bin</param>
        /// <returns></returns>
        public bool Loading_OpenUpdataFile(string filepath)
        {
            string typ = filepath.Substring(filepath.LastIndexOf('.'));  //在字符串中找到 ‘.’
            typ = typ.ToUpper();
            if ((typ == ".HEX") || (typ == ".IHEX"))
            {
                if (Loading_OpenHexFile(filepath))
                {
                    _UpdataFilePath = filepath;
                    _UpdataFileTyp = 1;
                    return true;
                }
                return false;
            }
            else if (typ == ".BIN")
            {
                if (Loading_OpenBinFile(filepath))
                {
                    _UpdataFilePath = filepath;
                    _UpdataFileTyp = 2;
                    return true;
                }
                return false;
            }
            MessageBox.Show("不支持的文件格式，请打开HEX或者BIN文件");
            _UpdataFileTyp = -1;
            Listbuff.Clear(); //删除所有元素
            return false;
        }
        /// <summary>
        /// 装载HEX文件并解析数据 
        /// </summary>
        /// <param name="Hexpath"></param>
        /// <returns></returns>
        private bool Loading_OpenHexFile(string Hexpath)
        {
            if (File.Exists(Hexpath) == false)
            {
                MessageBox.Show("文件不存在");
                return false;
            }
            Debug.WriteLine("文件大小:" + GetFileSize(Hexpath));   //显示文件大小
            Listbuff.Clear(); //删除所有元素
            StreamReader F_Reader = new StreamReader(Hexpath); //创建一个读文件的流
            string OneData;
            byte[] Bytebuff = new byte[1];
            UInt32 Flashaddr = 0, Nextaddr = 0;  //FLASH当前地址  及下一个地址
            Byte firstaddr = 0;
            while ((OneData = F_Reader.ReadLine()) != null)  //读取一行的数据
            {
                //DebugLog.CDebugLog.Debug_OutPut("读一行数据:" + OneData);
                byte[] Data = System.Text.Encoding.UTF8.GetBytes(OneData);  //把读取的数据转换成字节数组
                int err = 0;
                if ((err = TextOneHexData(Data, ref Bytebuff)) > 0)  //测试一行HEX文件数据 并解析
                {
                    switch (err)
                    {
                        case 0:
                            break;
                        default:
                            MessageBox.Show("Hex文件格式错误");
                            F_Reader.Close();
                            return false;
                    }
                }

                switch (Bytebuff[3])  //记录类型
                {
                    case 0X00: //数据记录
                        //得到FLASH中的地址 低两个字节
                        Flashaddr = (Flashaddr & 0xffff0000) | (((UInt32)Bytebuff[1] << 8) | Bytebuff[2]);
                        if ((firstaddr & 0x03) == 0x01)  //如果是第一次设置地址  则直接赋值
                        {
                            firstaddr |= 0x03; //标记已经设置过地址  则接下来的地址必须为连贯的
                            Nextaddr = Flashaddr;  //直接设置地址
                            FlashStaraddr = Nextaddr; //得到FLASH 的起始地址
                        }
                        else if ((firstaddr & 0x03)==0X00)  //如果没有扩展线性地址而直接是数据记录  则标记第一条的地址为FLASH起始地址
                        {
                            firstaddr |= 0x03; //标记已经设置过地址  则接下来的地址必须为连贯的
                            Nextaddr = Flashaddr;  //直接设置地址
                            FlashStaraddr = Nextaddr; //得到FLASH 的起始地址              
                        }

                        if (Flashaddr != Nextaddr)  //如果数据地址不等
                        {
                            F_Reader.Close();
                            MessageBox.Show("Hex文件FLASH地址不连续");
                            return false;
                        }
                        //把数据添加到数据缓存
                        for (uint i = 0; i < Bytebuff[0]; i++)
                        {
                            Listbuff.Add(Bytebuff[4 + i]);
                        }
                        Nextaddr += Bytebuff[0]; //地址偏移
                        unchecked  //不检测溢出
                        {
                            firstaddr &= (byte)(~0x80); //清楚乒乓标记
                        }
                        break;
                    case 0X01: //文件结束记录 
                        firstaddr |= 0x08; //标记文件结束
                        break;
                    case 0X02: //扩展段地址记录
                        firstaddr |= 0x40;  //测试
                        break;
                    case 0X03: //起始段地址类型记录
                        firstaddr |= 0x40;  //测试
                        break;
                    case 0X04: //扩展线性地址
                        if ((Bytebuff[1] != 0) || (Bytebuff[2] != 0))
                        {
                            F_Reader.Close();
                            MessageBox.Show("扩展线性地址错误");
                            return false; ; //退出
                        }
                        Nextaddr = 0;
                        for (uint i = 0; i < ((uint)Bytebuff[0]); i++)  //重新设置地址 高2位
                        {
                            Nextaddr <<= 8;
                            Nextaddr |= (UInt32)Bytebuff[4 + i];
                        }
                        Nextaddr <<= 16;  //左移16  此记录为高两字节
                        Flashaddr = Nextaddr;  //备份地址
                        firstaddr |= 0X80;     //标记为接收到扩展线性地址   
                        if ((firstaddr & 0X03) == 0) //如果是第一次设置地址  则表示为FLASH的起始地址
                        {
                            firstaddr |= 0X01;  //标记接收到
                        }
                        break;
                    case 0X05://起始线性地址类型记录
                        firstaddr |= 0x40;  //测试
                        break;
                    default:
                        return false;
                }
            }
            F_Reader.Close();
            return true;
        }

        /// <summary>
        /// 保存Bin文件数据
        /// </summary>
        /// <param name="Binpath"></param>
        /// <returns></returns>
        public bool SaveCreateBinFile(string Binpath)
        {
            if (GetFileListDataLoading() == false)
            {
                MessageBox.Show("没有打开HEX文件");
                return false;
            }
            ResetLoadingUpdataFile = true; //重载一次
            FileStream file = File.Create(Binpath);  //创建文件 并创建数据流
            byte[] filedata = new byte[DataList.Count];
            DataList.CopyTo(filedata, 0);  //读取LIST 中的数据
            file.Write(filedata, 0, filedata.Length);  //全部写入文件
            file.Close();  //关闭流
            Debug.WriteLine("保存的文件大小:" + filedata.Length);
            return true;
        }

        /// <summary>
        /// 装载Bin文件数据
        /// </summary>
        /// <param name="Binpath"></param>
        /// <returns></returns>
        private bool Loading_OpenBinFile(string Binpath)
        {
            if (File.Exists(Binpath) == false)
            {
                MessageBox.Show("文件不存在");
                return false;
            }
            //已读的方式打开文件  并创建数据流
            FileStream file = File.OpenRead(Binpath);
            byte[] Bindata = new byte[file.Length];
            file.Read(Bindata, 0, Bindata.Length); //读取全部数据
            file.Close(); //关闭数据流
            DataList.Clear(); //删除所有数据
            foreach (byte Data in Bindata)  //添加到链表中 
            {
                DataList.Add(Data);
            }
            return true;
        }
    }
}
