using System;
using System.Diagnostics;


namespace TS_Troubleshooter_Lite
{
    internal class TS_Integration
    {
        //defines if app is being run in Task Sequence environment by checking if it can populate from a variable
        public bool IsTSEnv()
        {
            string TSName = GetTSVar("_SMSTSPackageName");
            if (TSName == null) { return false; }
            else { return true; }
        }

        //setting a task sequence variable
        public void SetTSVar(string name, string value)
        {
            dynamic tsEnvironment = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.SMS.TSEnvironment"));
            tsEnvironment[name] = value;
        }
        //getting a task sequence varaible
        public string GetTSVar(string name)
        {
            try
            {
                dynamic tsEnvironment = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.SMS.TSEnvironment"));
                return tsEnvironment[name];
            }
            catch
            {
                return null;
            }
        }
        //closes tsprogressUI before launching
        public void TSProgressKill()
        {
            if (IsTSEnv())
            {
                Process[] processes = Process.GetProcessesByName("TSProgressUI");
                foreach (Process proc in processes)
                {
                    proc.Kill();
                }
            }
        }
    }

}
