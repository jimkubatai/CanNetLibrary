using System;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using Ixxat.Vci3;
using Ixxat.Vci3.Bal;
using Ixxat.Vci3.Bal.Can;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace CanConNet
{

    public partial class Form1 : Form
    {
        //public static CanConNet.vcican CANsrv = new CanConNet.vcican();
        
        public Form1()
        {
            
            InitializeComponent();
            
        }

        public void CanMsgReceived()
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {

            listBox1.Items.Clear();
            string s = CanServer.vcican.CheckDevice();
            string[] s0 = s.Split(';');
            for (int i = 0; i < s0.Length; i++)
                listBox1.Items.Add(s0[i]);
                                   
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            
      
        }

        private void button1_Click(object sender, EventArgs e)
        {
          bool b = CanServer.vcican.InitDevice(0, 250, (byte)listBox1.SelectedIndex);
          if (b == true)
              label1.Text = "Адаптер " + listBox1.SelectedIndex + " " + listBox1.Items[listBox1.SelectedIndex].ToString() + " инициализирован";
          backgroundWorker1.RunWorkerAsync();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
            string s = CanServer.vcican.CheckDevice();
            string[] s0 = s.Split(';');
            for (int i = 0; i < s0.Length; i++)
                listBox1.Items.Add(s0[i]);
                                   
        }

        private void button3_Click(object sender, EventArgs e)
        {
            byte[] b = new byte[8];
            CanServer.vcican.TransmitData(b);
        }

        public void AddInConsole(string s)
        {
            eConsole.Items.Add(s);
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true == true)
            if (CanServer.vcican.IsOpen == true)
            {
                
                string s = CanServer.vcican.ReceiveThreadFunc();
                Action<string> action = AddInConsole;
                if (s != "")
                {
                    if (InvokeRequired)
                    {

                        Invoke(action, s);

                    }

                    else
                    {

                        action(s);

                    }
                }
            }

        }

        
    }
}
