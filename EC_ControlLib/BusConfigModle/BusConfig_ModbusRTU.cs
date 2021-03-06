﻿using ControllerLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ControllerLib.BusConfigModle
{
    [Serializable()]
   
    public class BusConfig_ModbusRTU : BusConfigBase
    {
        public override EnumBusType BusType { get; } = EnumBusType.ModbusRTU;
        public override string Name { get; protected set; } = "HL6801";

        /// <summary>
        /// 默认文件名
        /// </summary>
        public override string Type { get; protected set; } = "ModbusRTU Coupler V1.0";

        /// <summary>
        /// 是什么类型的总线
        /// </summary>
        public override string ShortName { get; protected set; } = "MB excel";
        public BusConfig_ModbusRTU()
        {
        }
        protected BusConfig_ModbusRTU(SerializationInfo info, StreamingContext context) : base(info, context)
        {

        }
    }
}
