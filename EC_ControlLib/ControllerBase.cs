﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerLib
{
    public abstract class ControllerBase
    {
        public abstract bool Open(string Port);

        public abstract void Connect();

        public abstract List<string> GetModuleList();


        /// <summary>
        /// PureNameList
        /// </summary>
        /// <param name="ModuleNameList"></param>
        /// <returns></returns>
        public abstract bool SendModuleList(List<string> ModuleNameList);

        public abstract void GetModuleValue(out List<int> InputValueList, out List<int> OutputValueList);


        public abstract void SetModuleValue(List<int> OutputValueList);

        public abstract void CLose();


        public abstract bool IsOpen();
        
    }
}