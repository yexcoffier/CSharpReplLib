﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CSharpReplLib.VSCode;

namespace CSharpReplLib.WpfSample
{
    /// <summary>
    /// Interaction logic for ReplWindow.xaml
    /// </summary>
    public partial class ReplWindow : Window
    {
		public Visibility WaitingResult
		{
			get { return (Visibility)GetValue(WaitingResultProperty); }
			set { SetValue(WaitingResultProperty, value); }
		}
		public static readonly DependencyProperty WaitingResultProperty =
			DependencyProperty.Register("WaitingResult", typeof(Visibility), typeof(ReplWindow), new PropertyMetadata(Visibility.Collapsed));


		private List<string> _scriptsHistory = new List<string>();
        private int _historyIndex = 0;

        private ScriptHandler _scriptHandler;
        private VSCodeWriter _vsCodeWriter;

		private CancellationTokenSource _tokenSource;

		public ObservableCollection<ScriptHandler.ScriptResult> History { get; } = new ObservableCollection<ScriptHandler.ScriptResult>();

        public ReplWindow()
        {
            InitializeComponent();

            
            _scriptHandler = new ScriptHandler(func => Dispatcher.Invoke(func))
                .AddGlobals
                (
                    ("CurrentWindow", this)
                )
                .AddReferences
                (
                    Assembly.GetExecutingAssembly(),
                    includeReferencedAssemblies: true
                )
                .AddUsings
                (
                    "CSharpReplLib.WpfSample",
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "System.Reflection",
                    "System.Windows",
                    "System.Windows.Documents",
                    "System.Windows.Input",
                    "System.Windows.Media"
                );

            _scriptHandler.ScriptResultReceived += ScriptHandler_ScriptResultReceived;
            _scriptHandler.ScriptExecuted += ScriptHandler_ScriptExecuted;

            Task.Run(() => _scriptHandler.InitScript());
        }

        private async void ScriptTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;

					History.Add(new ScriptHandler.ScriptResult ( result : $"> {ScriptTextBox.Text}" ));
                    //HistoryText.Document.Blocks.Add(new Paragraph(new Run($"> {ScriptTextBox.Text}")));

                    _scriptsHistory.Add(ScriptTextBox.Text);
                    _historyIndex = _scriptsHistory.Count;

					var request = ScriptTextBox.Text;

					ScriptTextBox.Text = string.Empty;

					WaitingResult = Visibility.Visible;
					await _scriptHandler.ExecuteCode(request, GetToken());
					WaitingResult = Visibility.Collapsed;

					ScriptTextBox.Focus();

					break;

                case Key.Up:
                    _historyIndex = Math.Max(0, _historyIndex - 1);
                    if (_scriptsHistory.Count > _historyIndex)
                    {
                        ScriptTextBox.Text = _scriptsHistory[_historyIndex];
                        ScriptTextBox.CaretIndex = ScriptTextBox.Text.Length;
                    }

                    e.Handled = true;
                    break;

                case Key.Down:
                    _historyIndex = Math.Min(_scriptsHistory.Count, _historyIndex + 1);
                    if (_scriptsHistory.Count > _historyIndex)
                    {
                        ScriptTextBox.Text = _scriptsHistory[_historyIndex];
                        ScriptTextBox.CaretIndex = ScriptTextBox.Text.Length;
                    }
                    else
                        ScriptTextBox.Text = string.Empty;

                    e.Handled = true;
                    break;
            }
        }

		private CancellationToken GetToken()
		{
			_tokenSource?.Cancel();
			_tokenSource = new CancellationTokenSource();
			return _tokenSource.Token;
		}

        private void ScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ScriptTextBox.Focus();
        }

        private void ScriptHandler_ScriptResultReceived(object sender, ScriptHandler.ScriptResult e)
        {
			Dispatcher.Invoke(() => AddScriptResult(e));
		}

		private void ScriptHandler_ScriptExecuted(object sender, ScriptHandler.ScriptRequest e)
		{
			if (e.Writer != null && e.Writer == _vsCodeWriter)
				Dispatcher.Invoke(() => AddScriptResult(new ScriptHandler.ScriptResult ( result : "> Execute script from VS code...", isError : false )));
		}

        private void OpenVsCode_Click(object sender, RoutedEventArgs e)
        {
            if (_vsCodeWriter == null)
            {
                _vsCodeWriter = new VSCodeWriter();
                _vsCodeWriter.Open(_scriptHandler);
            }
        }

        private void AddScriptResult(ScriptHandler.ScriptResult result)
        {
			History.Add(result);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _scriptHandler.ScriptResultReceived -= ScriptHandler_ScriptResultReceived;
            _scriptHandler.ScriptExecuted -= ScriptHandler_ScriptExecuted;

            _vsCodeWriter?.Dispose();
        }
    }
}
