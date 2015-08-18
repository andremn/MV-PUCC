﻿using LPD.VirtualMachine.Engine;
using LPD.VirtualMachine.Engine.HAL;
using LPD.VirtualMachine.ViewModel;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static System.IO.Path;

namespace LPD.VirtualMachine.View
{
    /// <summary>
    /// Interaction logic for ExecutionWindow.xaml
    /// </summary>
    public partial class ExecutionWindow : MetroWindow, IInputProvider, IOutputProvider, IProgramExecutor
    {
        private const string FatalErrorMessageBoxTitle = "Erro!";
        private const string FinishedMessageBoxTitle = "Fim da execução";
        private const string FinishedMessageBoxContent = "O programa chegou ao fim da execução sem erros.";
        private const string InputEnterValueText = "Entre com um valor: ";
        private const string BackspaceString = "\b";
        private const int DefaultMessageBoxDelay = 500;

        private StringBuilder _inputBuffer;
        private EventWaitHandle _inputSynchronizer;
        private EventWaitHandle _executionSynchronizer;
        private bool _hasStartedExecution = false;

        /// <summary>
        /// Gets the current execution context.
        /// </summary>
        public Engine.ExecutionContext Context { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExecutionWindow"/> class with the specified program path.
        /// <param name="filePath">The path of the program going to be executed.</param>
        /// <param name="context">The execution context.</param>
        /// </summary>
        public ExecutionWindow(string filePath, Engine.ExecutionContext context)
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            Title += GetFileNameWithoutExtension(filePath);
            InstructionsDataGrid.DataContext = ConvertInstructionsToInstructionViewModel(filePath);
            Context = context;
            Context.Memory.StackRegion.Changed += OnStackChanged;
            _inputSynchronizer = new EventWaitHandle(false, EventResetMode.AutoReset);
            _inputBuffer = new StringBuilder();
        }

        /// <summary>
        /// Called when the caller is starting its execution.
        /// </summary>
        public void OnInstructionExecuting()
        {
            if (Context.Mode == ExecutionMode.Debug)
            {
                _executionSynchronizer.WaitOne();
            }
            else
            {
                if (Dispatcher.Invoke<bool>(CurrentCellContainsBreakpoint))
                {
                    Context.Mode = ExecutionMode.Debug;
                    _executionSynchronizer.WaitOne();
                }
            }
        }

        /// <summary>
        /// Called when the caller finished executing the current instruction.
        /// </summary>
        public void OnInstructionExecuted()
        {
            Dispatcher.Invoke(DoNextInstruction);
        }

        /// <summary>
        /// Called when the caller finished its execution.
        /// </summary>
        public void OnFinished()
        {
            Dispatcher.Invoke(async () =>
            {
                DoFinished();
                await ShowFinishedMessageAsync();
            });
        }

        /// <summary>
        /// Called when the caller finished its executin due to a faltal error.
        /// </summary>
        /// <param name="error">The error data.</param>
        public void OnFatalError(string error)
        {
            Dispatcher.Invoke(async () =>
            {
                NextInstructionButton.IsEnabled = ExecuteToEndButton.IsEnabled = false;
                await ShowFaltalErrorMessageAsync(error);
            });
        }

        /// <summary>
        /// Reads the next input value.
        /// </summary>
        /// <returns>The next input value.</returns>
        public int ReadInputValue()
        {
            Dispatcher.Invoke(() =>
            {
                NextInstructionButton.IsEnabled = false;
                AppendLineToOutput(InputEnterValueText);
            });
            
            TextCompositionManager.AddTextInputHandler(this, OnTextComposition);
            //Since the CPU execution is not done on the UI thread, this will not block the UI
            _inputSynchronizer.WaitOne();

            int ret = int.Parse(_inputBuffer.ToString());

            _inputBuffer.Clear();
            return ret;
        }

        /// <summary>
        /// Writes the specified value to th output.
        /// </summary>
        /// <param name="value">The value to output.</param>
        public void Print(int value)
        {
            Dispatcher.Invoke(() => AppendLineToOutput(value.ToString()));
        }

        private bool CurrentCellContainsBreakpoint()
        {
            InstructionViewModel model = InstructionsDataGrid.Items[Context.ProgramCounter.Current] as InstructionViewModel;

            if (model == null)
            {
                return false;
            }

            return model.HasBreakpoint;
        }

        /// <summary>
        /// Informs the user the execution finished.
        /// </summary>
        /// <returns><see cref="Task"/></returns>
        private async Task ShowFinishedMessageAsync()
        {
            //Shows the messagebox.
            await this.ShowMessageAsync(FinishedMessageBoxTitle, FinishedMessageBoxContent);
        }

        /// <summary>
        /// Shows a message box for a fatal error.
        /// </summary>
        /// <param name="message">The fatal error message.</param>
        /// <returns><see cref="Task"/></returns>
        private async Task ShowFaltalErrorMessageAsync(string message)
        {
            await this.ShowMessageAsync(FatalErrorMessageBoxTitle, message);
        }

        private void DoNextInstruction()
        {
            int currentInstructionAddress = Context.ProgramCounter.Current;

            InstructionsDataGrid.SelectedIndex = currentInstructionAddress;
            InstructionsDataGrid.ScrollIntoView(InstructionsDataGrid.Items[currentInstructionAddress]);
        }

        /// <summary>
        /// Sets the window to a finished state.
        /// </summary>
        private void DoFinished()
        {
            _hasStartedExecution = false;

            int index = InstructionsDataGrid.Items.Count - 1;

            InstructionsDataGrid.SelectedIndex = index;
            InstructionsDataGrid.ScrollIntoView(InstructionsDataGrid.Items[index]);
            ExecuteToEndButton.IsEnabled = true;
        }

        /// <summary>
        /// Writes a line to the output window.
        /// </summary>
        /// <param name="line">The line to be writen.</param>
        private void AppendLineToOutput(string line)
        {
            OutputListView.Items.Add(line);
        }

        /// <summary>
        /// Starts the program execution.
        /// </summary>
        private void Start(ExecutionMode mode)
        {
            //Clears all the data we used just to make sure we don't get data from previous exeution.
            Context.Memory.StackRegion.Clear();
            Context.ProgramCounter.Jump(CPU.InitialProgramCounter);
            //Collect some garbage...
            GC.Collect();
            //Clears the output window
            OutputListView.Items.Clear();
            _executionSynchronizer = new EventWaitHandle(false, EventResetMode.AutoReset);
            InstructionsDataGrid.SelectedIndex = 0;
            Context.Mode = mode;
            //We starting the execution...
            _hasStartedExecution = true;
             //... now!!
            CPU.Instance.BeginExecution(this);
        }

        /// <summary>
        /// Get the value for a <see cref="DisplayNameAttribute"/> object.
        /// </summary>
        /// <param name="descriptor">The property info.</param>
        /// <returns></returns>
        private static string GetPropertyDisplayName(object descriptor)
        {
            var pd = descriptor as PropertyDescriptor;

            if (pd != null)
            {
                //Check for DisplayName attribute and set the column header accordingly.
                DisplayNameAttribute displayName = pd.Attributes[typeof(DisplayNameAttribute)] as DisplayNameAttribute;

                if (displayName != null && displayName != DisplayNameAttribute.Default)
                {
                    return displayName.DisplayName;
                }

            }
            else
            {
                PropertyInfo propertyInfo = descriptor as PropertyInfo;

                if (propertyInfo != null)
                {
                    //Check for DisplayName attribute and set the column header accordingly.
                    object[] attributes = propertyInfo.GetCustomAttributes(typeof(DisplayNameAttribute), true);

                    for (int i = 0; i < attributes.Length; ++i)
                    {
                        DisplayNameAttribute displayName = attributes[i] as DisplayNameAttribute;

                        if (displayName != null && displayName != DisplayNameAttribute.Default)
                        {
                            return displayName.DisplayName;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Handles the changes of the current stack.
        /// </summary>
        /// <param name="reason">The reason the stack changed.</param>
        private void HandleStackChanged(StackChangedEventArgs args)
        {
            switch (args.Reason)
            {
                //The stack was completely ereased.
                case StackChangedReason.Cleared:
                    StackListView.Items.Clear();
                    break;
                //Something was inserted in the stack.
                case StackChangedReason.Inserted:
                    int index = args.Index.Value;

                    StackListView.Items.RemoveAt(index);
                    StackListView.Items.Insert(index, Context.Memory.StackRegion.LoadFrom(index));
                    break;
                //Something was removed from the stack.
                case StackChangedReason.Popped:
                    StackListView.Items.RemoveAt(StackListView.Items.Count - 1);
                    break;
                //Something was added to the stack.
                case StackChangedReason.Pushed:
                    StackListView.Items.Add(Context.Memory.StackRegion.Load());
                    break;
            }
        }

        /// <summary>
        /// Updates the output with was inputed.
        /// </summary>
        private void UpdateInputLineOnOutput()
        {
            int index = OutputListView.Items.Count - 1;

            OutputListView.Items[index] = InputEnterValueText + _inputBuffer;
        }

        /// <summary>
        /// Occurs when a column of a DataGrid is being automatically generated.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            var displayName = GetPropertyDisplayName(e.PropertyDescriptor);

            if (!string.IsNullOrEmpty(displayName))
            {
                e.Column.Header = displayName;

                if (displayName != "Breakpoint")
                {
                    e.Column.IsReadOnly = true;
                }
            }

        }

        /// <summary>
        /// Occurs when the user press a key.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The data of the event.</param>
        private void OnTextComposition(object sender, TextCompositionEventArgs e)
        {
            string text = e.Text;

            if (text == BackspaceString)
            {
                _inputBuffer.Remove(_inputBuffer.Length - 1, 1);
                UpdateInputLineOnOutput();
                return;
            }

            _inputBuffer.Append(e.Text);
            Dispatcher.Invoke(UpdateInputLineOnOutput);
        }

        /// <summary>
        /// Occurs when the window is loaded and displayed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event's info.</param>
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// Occurs when the stack has changed its contents.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The data of the event.</param>
        private void OnStackChanged(object sender, StackChangedEventArgs e)
        {
            Dispatcher.Invoke(() => HandleStackChanged(e));
        }

        /// <summary>
        /// Converts the instructions in the speciified file to a collection of <see cref="InstructionViewModel"/>.
        /// </summary>
        /// <param name="filePath">The path of the file containing the instructions.</param>
        /// <returns>A collection of <see cref="InstructionViewModel"/>.</returns>
        private IList<InstructionViewModel> ConvertInstructionsToInstructionViewModel(string filePath)
        {
            string[] instructions = InstructionSet.CreateFromFile(filePath, false).Instructions;
            IList<InstructionViewModel> instructionViewModel = new List<InstructionViewModel>(instructions.Length);

            for (uint i = 0; i < instructions.Length; i++)
            {
                string instruction = instructions[i];

                instructionViewModel.Add(new InstructionViewModel()
                {
                    Comment = "Instrução",
                    //Removes various spaces and tabs and replace them by two tabs.
                    Content = Regex.Replace(instruction, @"(?:\s+|\t+)", "\t\t"),
                    LineNumber = i
                });
            }

            return instructionViewModel;
        }

        /// <summary>
        /// Occurs when the NextInstruction button is clicked.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The data of the event.</param>
        private void OnNextInstructionButtonClick(object sender, RoutedEventArgs e)
        {
            if (!_hasStartedExecution)
            {
                Start(ExecutionMode.Debug);
                return;
            }

            _executionSynchronizer.Set();
        }

        /// <summary>
        /// Occurs when the ExecuteToEnd button is clicked.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The data of the event.</param>
        private void OnExecuteToEndButtonClick(object sender, RoutedEventArgs e)
        {
            if (!_hasStartedExecution)
            {
                Start(ExecutionMode.Normal);
                return;
            }

            Context.Mode = ExecutionMode.Normal;
            _executionSynchronizer.Set();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NextInstructionButton.IsEnabled = true;
                TextCompositionManager.RemoveTextInputHandler(this, OnTextComposition);
                _inputSynchronizer.Set();
            }
        }
    }
}
