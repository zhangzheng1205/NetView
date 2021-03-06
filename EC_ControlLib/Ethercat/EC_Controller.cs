﻿using ControllerLib.Ethercat.ModuleConfigModle;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerLib.Ethercat
{
	public class EC_Controller : ControllerBase
	{
		#region Field
		SerialPort Comport = new SerialPort();
		object ComportLock = new object();
		List<ModuleConfigModleBase> ModuleList = new List<ModuleConfigModleBase>();
		private bool isConnected = false;

		public override event EventHandler<bool> OnConnectStateChanged;
		#endregion

		#region UserAPI
		public EC_Controller() {
			IsConnected = false;
		}
		public override bool IsConnected
		{
			get { return isConnected; }
			protected set{
				if (isConnected != value)
				{
					OnConnectStateChanged?.Invoke(this, value);
					isConnected = value;
				}
			}
		}

        public override byte ControllerID => 0x05;

        public override bool Open(string Port)
        {
            if (Comport.IsOpen)
                Comport.Close();
            Comport.PortName = Port.ToUpper();
            Comport.BaudRate = 115200;
            Comport.Parity = Parity.Even;
            Comport.DataBits = 8;
            Comport.StopBits = StopBits.One;
            Comport.ReadTimeout = 1000;
            Comport.WriteTimeout = 1000;
            Comport.ReadBufferSize = 1024;
            Comport.WriteBufferSize = 1024;
          
            Comport.Open();
            Comport.DiscardInBuffer();
            Comport.DiscardOutBuffer();
            return Comport.IsOpen;
        }

        public override bool Connect()
        {
			byte[] Cmd = new byte[] { 0x68, 0x06, 0x01, 0x02, 0x68, ControllerID };
			var Crc = CRC16(Cmd, 0, Cmd.Length);
			List<byte> FinalCmd = new List<byte>(Cmd);
			FinalCmd.Add(Crc[1]);
			FinalCmd.Add(Crc[0]);
			try
			{
				for (int i = 0; i < 3; i++)
				{
					lock (ComportLock)
					{
						Comport.Write(FinalCmd.ToArray(), 0, FinalCmd.Count);
						IsConnected = ReadVoidAck(new byte[] { 0x68, 0x06, 0x01, 0x02, 0x68, ControllerID });
						if (IsConnected)
							return true;
					}
				}
				return false;
			}
			catch
			{
				IsConnected = false;
				return false;
			}
        }

        public override bool DisConnect()
        {
            //68 06 01 FF 68 05 CRC
            byte[] Cmd = new byte[] { 0x68, 0x06, 0x01, 0xFF, 0x68, ControllerID };
            
            var Crc = CRC16(Cmd, 0, Cmd.Length);
            List<byte> FinalCmd = new List<byte>(Cmd);
            FinalCmd.Add(Crc[1]);
            FinalCmd.Add(Crc[0]);
			for (int i = 0; i < 3; i++)
			{
				lock (ComportLock)
				{
					Comport.Write(FinalCmd.ToArray(), 0, FinalCmd.Count);
					IsConnected = false;
					//Expected Ack
					var ExpectedAck = new byte[] { 0x68, 0x06, 0x01, 0x02, 0x68, ControllerID };
					if (ReadVoidAck(ExpectedAck))
					{
						IsConnected = false;
						return true;
					}
				}
			}
			return false;
        }


        /// <summary>
        /// 获取模块信息列表，从控制器读取
        /// </summary>
        /// <returns></returns>
        public override List<ModuleConfigModleBase> GetModuleList()
        {
            //68 08 01 02 68 05 69 96 CRC
            byte[] Cmd = new byte[] { 0x68, 0x08, 0x01,0x02, 0x68, ControllerID, 0x69, 0x96 };
            var Crc = CRC16(Cmd, 0, Cmd.Length);
            List<byte> FinalCmd = new List<byte>(Cmd);
            FinalCmd.Add(Crc[1]);
            FinalCmd.Add(Crc[0]);
            lock (ComportLock)
            {
                Comport.Write(FinalCmd.ToArray(), 0, FinalCmd.Count);
                return ReadModuleListAck();
            }
        }


        /// <summary>
        /// PureNameList， 将配置信息发送给控制器
        /// 68 N 01 02 68 05 96 69 模块 1 参数 … 模块 N 参数 CRC（05 表示 EtherCAT，其他总线其他值）
        /// </summary>
        /// <param name="ModuleInfoList"></param>
        /// <returns></returns>
        public override bool SendModuleList(List<ModuleConfigModleBase> ModuleInfoList)
        {
            //68 N 01 02 68 05 96 69
            byte[] CmdHeader = new byte[] { 0x68, 0x08, 0x01, 0x02, 0x68, ControllerID, 0x96, 0x69 };
            IEnumerable<byte> CmdSend = new List<byte>(CmdHeader);
            foreach (var it in ModuleInfoList)
            {
                var B = it.ToByteArr();
                CmdSend=CmdSend.Concat(B);
            }
			List<byte> FinalCmd = new List<byte>(CmdSend);
			FinalCmd[1] = (byte)FinalCmd.Count;
			var Crc = CRC16(FinalCmd.ToArray(), 0, FinalCmd.Count());
                
            FinalCmd.Add(Crc[1]);
            FinalCmd.Add(Crc[0]);
			for (int i = 0; i < 3; i++)
			{
				lock (ComportLock)
				{
					Comport.Write(FinalCmd.ToArray(), 0, FinalCmd.Count);
					if (ReadVoidAck(new byte[] { 0x68, 0x08, 0x01, 0x02, 0x68, ControllerID, 0x69, 0x96 }))
						return true;
				}
			}
			return false;
        }

        /// <summary>
        /// 读取模块的值
        /// 68 N 01 02 N M D0….D n CRC
        /// 其中，68 是固定数据头；01 是组态软件站号，02 是耦合器站号，都固定；N 是实
        ///  际组态的输出字节数；M 是实际组态的输入字节数
        /// </summary>
        /// <param name="InputValueList"></param>
        /// <param name="OutputValueList"></param>
        public override bool GetModuleValue(List<uint> ModifyValueList, out List<uint> InputValueList, out List<uint> OutputValueList)
        {
            InputValueList = new List<uint>();
            OutputValueList = new List<uint>();

            byte N = 0;
            byte M = 0;
            byte Len = 0;

            //说明已经获取到Module的列表
            foreach (var it in ModuleList)
            {
                foreach (var Sub in it.ModuleSubInfoList)
                {
                    if (Sub.IOType == EnumModuleIoType.IN)
                    {
                        M += (byte)(Sub.BitSize / 8);
                    }
                    else
                    {
                        N += (byte)(Sub.BitSize / 8);
                    }        
                }
            }
            Len = (byte)(N + 6);
            byte[] CmdHeader = new byte[] { 0x68, Len, 0x01, 0x02, N, M};
            List<byte> CmdSend = new List<byte>(CmdHeader);
            for (int i = 0; i < N; i++)
            {
                CmdSend.Add(0x55);
            }
            
            var Crc = CRC16(CmdSend.ToArray(), 0, CmdSend.Count);
            List<byte> FinalCmd = new List<byte>(CmdSend);
            FinalCmd.Add(Crc[1]);
            FinalCmd.Add(Crc[0]);

			for (int i = 0; i < 3; i++)
			{
				lock (ComportLock)
				{
					Comport.Write(FinalCmd.ToArray(), 0, FinalCmd.Count);
					if (GetValueAck(out OutputValueList, out InputValueList))
						return true;
				}
			}

			return false;
           
        }

        /// <summary>
        /// 强制写入
        /// </summary>
        /// <param name="OutputValueList"></param>
        public override void SetModuleValue(List<uint> OutputValueList)
        {
            byte N = 0;
            byte M = 0;
            byte Len = 0;
            foreach (var it in ModuleList)
            {
                foreach (var Sub in it.ModuleSubInfoList)
                {
                    if (Sub.IOType == EnumModuleIoType.IN)
                    {
                        M += (byte)(Sub.BitSize / 8);
                    }
                    else
                    {
                        N += (byte)(Sub.BitSize / 8);
                    }
                }
            }
            Len = (byte)(N + 6);
            byte[] CmdHeader = new byte[] { 0x68, Len, 0x01, 0x02, N, M, 0x96, 0x69 };
            List<byte> CmdSend = new List<byte>(CmdHeader);

            var Crc = CRC16(CmdSend.ToArray(), 0, CmdSend.Count);
            List<byte> FinalCmd = new List<byte>(CmdSend);
            FinalCmd.Add(Crc[1]);
            FinalCmd.Add(Crc[0]);
            lock (ComportLock)
            {
                Comport.Write(FinalCmd.ToArray(), 0, FinalCmd.Count);
                
            }
        }

        public override bool Hearbeat()
        {
            return this.Connect();
            //throw new NotImplementedException();
        }

        public override void CLose()
        {
            lock (ComportLock)
            {
                Comport.Close();
                IsConnected = Comport.IsOpen;
            }
        }

     
        #endregion

        #region Private Method

        public byte[] CRC16(byte[] dataSrc, int offset, int datalength)
        {
            if (offset + datalength > dataSrc.Length)
                throw new Exception("data out of range");
            List<byte> list = new List<byte>();
            for (int i = offset; i < offset + datalength; i++)
            {
                list.Add(dataSrc[i]);
            }
            var data = list.ToArray();
            int len = data.Length;
            if (len > 0)
            {
                ushort crc = 0xFFFF;

                for (int i = 0; i < len; i++)
                {
                    crc = (ushort)(crc ^ (data[i]));
                    for (int j = 0; j < 8; j++)
                    {
                        crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
                    }
                }
                byte hi = (byte)((crc & 0xFF00) >> 8);  //高位置
                byte lo = (byte)(crc & 0x00FF);         //低位置

                return new byte[] { hi, lo };
            }
            return new byte[] { 0, 0 };
        }

     
        /// <summary>
        /// 无需填写CRC进去，会自动计算CRC是否正确
        /// </summary>
        /// <param name="ExpectAckByteList"></param>
        /// <returns></returns>
        bool ReadVoidAck(byte[] ExpectAckByteList, int Timeout=1000)
        {
            var StartTime = DateTime.Now.Ticks;
            List<byte> Recv = new List<byte>();
			List<byte> RecvFul = new List<byte>();
            bool IsHeaderFound = false;
            int Length = 0;
            while (true)
            {
				byte bt =0;
				if (Comport.BytesToRead > 0)
				{
					bt = (byte)Comport.ReadByte();
					RecvFul.Add(bt);
					if (bt == 0x68)
					{
						IsHeaderFound = true;
					}

					if (IsHeaderFound)
					{
						Recv.Add(bt);
						if (Recv.Count == 2)
							Length = Recv[1];
						if (Length == Recv.Count - 2)
						{
							if (Length == ExpectAckByteList.Length)
							{
								if (CompareList(Recv.ToArray(), 0, ExpectAckByteList, 0, Length))
								{
									var CrcCal = CRC16(Recv.ToArray(), 0, Length);
									if (CrcCal[0] == Recv[Length + 1] && CrcCal[1] == Recv[Length])
										return true;
									else
										throw new Exception("CRC check error");
								}
							}
						}
					}
				}
				if (TimeSpan.FromTicks(DateTime.Now.Ticks - StartTime).TotalMilliseconds > Timeout)
				{
					//Console.WriteLine($"IsHeaderFound ={IsHeaderFound}, RecvFul的长度{RecvFul.Count}");
					//foreach (var c in RecvFul)
					//	Console.WriteLine(string.Format("%2X , ", c));
					//throw new Exception($"Timeout to connect controller,recieve len;{RecvFul.Count}");
					return false;
				}
            }
        }

        /// <summary>
        /// PureName
        /// </summary>
        /// <returns></returns>
        List<ModuleConfigModleBase> ReadModuleListAck(int Timeout=1000)
        {
            var ModuleList = new List<ModuleConfigModleBase>();
            var StartTime = DateTime.Now.Ticks;
            List<byte> Recv = new List<byte>();
            bool IsHeaderFound = false;
            int Length = 0;
            while (true)
            {
				byte bt = 0;
				if (Comport.BytesToRead > 0)
				{
					bt = (byte)Comport.ReadByte();

					if (bt == 0x68)
					{
						IsHeaderFound = true;
					}
					if (IsHeaderFound)
					{
						Recv.Add(bt);
						if (Recv.Count == 2)
							Length = Recv[1];
						if (Length == Recv.Count - 2)   //接受完毕
						{
							var CrcCal = CRC16(Recv.ToArray(), 0, Length);
							if (CrcCal[0] == Recv[Length + 1] && CrcCal[1] == Recv[Length])
							{
								//68 0C 01 02 68 05 96 69 11 01 01 00 34 75
								ModuleList = GetModuleFromByteArr(Recv.ToArray(), 8, Recv.Count - 10);
								return ModuleList;
							}
							else
								return null;
						}
					}
				}
				if (TimeSpan.FromTicks(DateTime.Now.Ticks - StartTime).TotalMilliseconds > Timeout)
					return null;
			}
        }

        bool GetValueAck(out List<UInt32> OutputValueRecv,out List<UInt32> InputValueRecv, int Timeout=1000)
        {
            OutputValueRecv = new List<uint>();
            InputValueRecv = new List<uint>();
            //首先获取需要读取的输入输出的字节长度

            byte OutputBtLen = 0;
            byte InputBtLen = 0;
            foreach (var it in ModuleList)
            {
                foreach (var Sub in it.ModuleSubInfoList)
                {
                    if (Sub.IOType == EnumModuleIoType.IN)
                    {
                        InputBtLen += (byte)(Sub.BitSize / 8);
                    }
                    else
                    {
                        OutputBtLen += (byte)(Sub.BitSize / 8);
                    }
                }
            }

            var StartTime = DateTime.Now.Ticks;
            List<byte> Recv = new List<byte>();
            bool IsHeaderFound = false;
            int Length = 0;
            while (true)
            {
				byte bt = 0;
				if (Comport.BytesToRead > 0)
				{

					bt = (byte)Comport.ReadByte();


					if (bt == 0x68)
					{
						IsHeaderFound = true;
					}
					if (IsHeaderFound)
					{
						Recv.Add(bt);
						if (Recv.Count == 2)
							Length = Recv[1];
						if (Length == Recv.Count - 2 && Length - 2 == OutputBtLen + InputBtLen)   //接受完毕
						{
							var CrcCal = CRC16(Recv.ToArray(), 0, Length);
							if (CrcCal[0] == Recv[Length + 1] && CrcCal[1] == Recv[Length])
							{

								int OutputStartPos = 2;
								int InputStartPos = 2 + OutputBtLen;

								//读取完毕赋值
								foreach (var it in ModuleList)
								{
									List<byte> BtArrTotalInModule = new List<byte>();
									foreach (var Sub in it.ModuleSubInfoList)
									{
										if (Sub.IOType == EnumModuleIoType.IN)
										{
											for (int i = 0; i < Sub.BitSize / 8; i++)
												BtArrTotalInModule.Add(Recv[InputStartPos++ + i]);
										}
										else
										{
											for (int i = 0; i < Sub.BitSize / 8; i++)
												BtArrTotalInModule.Add(Recv[OutputStartPos++ + i]);
										}
									}
									it.GetSubModuleListValueFromBtArr(BtArrTotalInModule.ToArray(), 0, BtArrTotalInModule.Count);

									//返回值赋值
									foreach (var xx in it.ModuleSubInfoList)
									{
										if (xx.IOType == EnumModuleIoType.IN)
											InputValueRecv.Add(xx.RawData);
										else
											OutputValueRecv.Add(xx.RawData);
									}

								}
								return true;
							}
							else
								return false;
						}
					}
				}
                if (TimeSpan.FromTicks(DateTime.Now.Ticks - StartTime).TotalMilliseconds > Timeout)
                    return false;
            }
        }


        List<ModuleConfigModleBase> GetModuleFromByteArr (byte[] BtArr, int StartPos, int length)
        {
            if (StartPos + length > BtArr.Length)
                throw new Exception("Wrong length parametr when parse Module from ByteArr");
            List<ModuleConfigModleBase> ModuleList = new List<ModuleConfigModleBase>();
            ModuleConfigModleBase ModuleInfo = null;
            int iPos = StartPos;
            while (iPos < length + StartPos)
            {
                byte ModuleType = BtArr[iPos];
                switch (ModuleType)
                {
                    case 0x11:
                        ModuleInfo = new ModuleConfig_HL1001();                        
                        break;
                    case 0x21:
                        ModuleInfo = new ModuleConfig_HL2001();
                        break;
                    case 0x22:
                        ModuleInfo = new ModuleConfig_HL2002();
                        break;
                    case 0x23:
                        ModuleInfo = new ModuleConfig_HL2003();
                        break;
                    case 0x31:
                        ModuleInfo = new ModuleConfig_HL3001();
                        break;
                    case 0x32:
                        ModuleInfo = new ModuleConfig_HL3002();
                        break;
                    case 0x41:
                        ModuleInfo = new ModuleConfig_HL4001();
                        break;
                    case 0x42:
                        ModuleInfo = new ModuleConfig_HL4002();
                        break;
                    case 0x51:
                        ModuleInfo = new ModuleConfig_HL5001();
                        break;
                    case 0x52:
                        ModuleInfo = new ModuleConfig_HL5002();
                        break;
                    default:
                        throw new Exception("Wrong length parametr when parse Module from ByteArr");
                }
                List<byte> ModuleByteArr = new List<byte>();

                for (int i = 0; i < ModuleInfo.ByteArrayExpectLength; i++)
                    ModuleByteArr.Add(BtArr[iPos+i]);

                ModuleInfo.FromByteArray(ModuleByteArr.ToArray());
                iPos += ModuleInfo.ByteArrayExpectLength;
                ModuleList.Add(ModuleInfo);
            }
            return ModuleList;

        }



        bool CompareList(byte[] BtList1, int offset1, byte[] BtList2, int offset2, int length)
        {
            if (BtList1.Count() < offset1 + length || BtList2.Count() < offset2 + length)
                return false;
            bool bRet = true;
            for (int i = 0; i < length; i++)
            {
                bRet &= (BtList1[offset1 + i] == BtList2[offset2 + i]);
            }
            return bRet;
        }

        #endregion
    }
}
