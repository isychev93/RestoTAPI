using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using JulMar.Atapi;

namespace RestoTAPIMonitor
{
    public partial class MainForm : Form
    {
        private readonly object lockOnLog = new object();

        private const int COLUMNS_LINE = 1;
        private const int COLUMNS_CID = 2;
        private const int COLUMNS_STATE = 3;
        private const int COLUMNS_CALLER = 4;
        private const int COLUMNS_CALLED = 5;
        private const int COLUMNS_CONNECTED = 5;

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private TapiLine[] availableTapiLines;
        private TapiLine[] subscribedLines;

        private TapiManager tapiManager = new TapiManager("RESTOTAPITestToolMonitor");
        public MainForm()
        {

            InitializeComponent();
            buttonSubscribe.Enabled = false;
            buttonUnsubscribe.Enabled = false;

            log.Info("Start programm");

            Initialize();
        }

        private void Initialize()
        {
            log.Info("Try to initialize");

            tapiManager.NewCall += TapiManagerOnNewCall;
            tapiManager.CallInfoChanged += TapiManagerOnCallInfoChanged;
            tapiManager.CallStateChanged += TapiManagerOnCallStateChanged;
            var taskToInit = Task.Factory.StartNew(() =>
            {
                if (tapiManager.Initialize() == false)
                {
                    log.Info("Initialized failed.");
                    ShowError();
                }
            });

            taskToInit.ContinueWith(_ =>
           {
               availableTapiLines = tapiManager.Lines;
               log.InfoFormat("Found {0} lines", availableTapiLines.Length);
               availableTapiLines.ToList().ForEach(l =>
               {
                   log.InfoFormat("Line name {0}, line id {1}", l.Name, l.Id);
               });
               checkedListBox1.DataSource = availableTapiLines.Select(l => l.Name).ToArray();
               labelStatus.Text = "Initialized (select lines and subscribe)";
               buttonSubscribe.Enabled = true;

           }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.FromCurrentSynchronizationContext());

            taskToInit.ContinueWith(t =>
            {
                log.Info("Initialized failed.");

                if (t.Exception != null)
                    ShowError(t.Exception);
                else
                    ShowError();

            }, TaskContinuationOptions.NotOnRanToCompletion);
        }

        private void TapiManagerOnNewCall(object sender, NewCallEventArgs newCallEventArgs)
        {
            lock (lockOnLog)
            {
                var call = newCallEventArgs.Call;
                var line = call.Line;
                LogWithCallInfo(call, "New call added");
                LogFullCallInfo(call);

                UpdateList(line, call);
            }
        }

        private void TapiManagerOnCallInfoChanged(object sender, CallInfoChangeEventArgs callInfoChangeEventArgs)
        {
            lock (lockOnLog)
            {
                var call = callInfoChangeEventArgs.Call;
                var line = call.Line;

                LogWithCallInfo(call, "Call info changed");
                LogFullCallInfo(call);

                UpdateList(line, call);
            }
        }

        private void TapiManagerOnCallStateChanged(object sender, CallStateEventArgs callStateEventArgs)
        {
            lock (lockOnLog)
            {
                var call = callStateEventArgs.Call;
                var line = call.Line;

                LogWithCallInfo(call, "Call state changed");
                LogFullCallInfo(call);

                UpdateList(line, call);
            }
        }

        private void UpdateList(TapiLine line, TapiCall call)
        {
            Invoke(() =>
            {
                var existingItem = listView1.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Tag == line);

                if (existingItem == null)
                {
                    var listView = new ListViewItem(line.Name) { Tag = line };
                    listView.SubItems.AddRange(
                        new[]
                        {
                            call.Id.ToString(),
                            call.CallState.ToString(),
                            string.Format("{0} {1}", call.CallerId, call.CallerName),
                            string.Format("{0} {1}", call.CalledId, call.CalledName),
                            string.Format("{0} {1}", call.ConnectedId, call.ConnectedName)
                        });
                    listView1.Items.Add(listView);
                }
                else
                {
                    existingItem.SubItems[COLUMNS_STATE].Text = call.CallState.ToString();

                    if (call.CallState == CallState.Idle)
                    {
                        call.Dispose();
                        listView1.Items.Remove(existingItem);
                    }
                }
            });
        }

        private void Invoke(Action action)
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }
            action();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            labelStatus.Text = "Wait while start monitor lines";
            buttonSubscribe.Enabled = false;
            buttonUnsubscribe.Enabled = true;
            checkedListBox1.Enabled = false;
            checkBox1.Enabled = false;

            var selecedLines = availableTapiLines.Where((t, i) => checkedListBox1.GetItemChecked(i)).ToList();
            log.InfoFormat("Try to subscribe selected lines {0}", string.Join(", ", selecedLines));
            var taskToSubscribe = Task.Factory.StartNew(() =>
            {
                selecedLines.ForEach(l =>
                {
                    log.InfoFormat("Start monitor line {0}", l.ToString());
                    l.Monitor();
                });
                subscribedLines = selecedLines.ToArray();
            });
            taskToSubscribe.ContinueWith(_ =>
            {
                labelStatus.Text = "Subscribed (all fine!)";
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
            taskToSubscribe.ContinueWith(t =>
            {
                log.ErrorFormat("Error while try to start monitor lines");
                ShowError(t.Exception);
            }, TaskContinuationOptions.NotOnRanToCompletion);
        }

        private void ShowLineTree(TapiLine line, List<TapiLine> excepted)
        {
            log.InfoFormat("Tree node {0}", line.Name);
            excepted.Add(line);

            foreach (var subLine in line.Addresses.Select(l => l.Line))
            {
                if (excepted.Contains(subLine))
                    continue;

                ShowLineTree(subLine, excepted.ToList());
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            labelStatus.Text = "Unsubscribe (whait)";
            buttonUnsubscribe.Enabled = false;

            var taskToUnsubscribe = Task.Factory.StartNew(() =>
            {
                subscribedLines.ToList().ForEach(l =>
                {
                    l.Close();
                });
            });
            taskToUnsubscribe.ContinueWith(_ =>
            {
                labelStatus.Text = "Initialized (select lines and subscribe)";
                buttonSubscribe.Enabled = true;
                checkedListBox1.Enabled = true;
                checkBox1.Enabled = true;
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
            taskToUnsubscribe.ContinueWith(t =>
            {
                ShowError(t.Exception);

            }, TaskContinuationOptions.NotOnRanToCompletion);
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            for (var i = 0; i < availableTapiLines.Length; i++)
            {
                checkedListBox1.SetItemChecked(i, checkBox1.Checked);
            }
        }

        private void LogFullCallInfo(TapiCall call)
        {
            if (call == null)
                throw new ArgumentNullException("call");

            LogWithCallInfo(call, string.Format("Info by call {0} and state {1}", call.Id, call.CallState));
            LogWithCallInfo(call, string.Format("Owner Address {0}", call.Address.Address));
            LogWithCallInfo(call, string.Format("CalledId {0}, CalledName {1}", call.CalledId ?? string.Empty, call.CalledName ?? string.Empty));
            LogWithCallInfo(call, string.Format("CallerName {0}, CallerdId {1}", call.CallerId ?? string.Empty, call.CallerId ?? string.Empty));
            LogWithCallInfo(call, string.Format("ConnectedId {0}, ConnectedName {1}", call.ConnectedId ?? string.Empty, call.ConnectedName ?? string.Empty));
        }

        private void LogWithCallInfo(TapiCall call, string text)
        {
            log.InfoFormat(" {0} {1} " + text, call.Line.Name, call.Id);
        }

        private void ShowError(Exception exception = null)
        {
            Invoke(() =>
            {
                labelStatus.Text = "Some error occurred - restart app";

                buttonUnsubscribe.Enabled = false;
                buttonSubscribe.Enabled = false;

                checkedListBox1.Enabled = false;
                checkBox1.Enabled = false;
            });

            if (exception != null)
                log.Error(exception);
        }
    }
}
