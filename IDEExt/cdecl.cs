using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using System.Text;
using System.Text.RegularExpressions;

namespace IDEExt
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class cdecl
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("27e12901-74c2-4feb-a735-79512fd346f5");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="cdecl"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private cdecl(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static cdecl Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in cdecl's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new cdecl(package, commandService);
        }

        private bool Replace(ref string Text, string What, string With)
        {
            Text = Text.Replace(What, With);
            if (Text.Length == 0)
                return true;
            return false;
        }
        
        private string Translate(string text)
        {
            if (text.Contains("\n")) // должна быть одна строка
                return "";
            string res = "Declare ";
            text = Regex.Replace(text,@"(\s)\s+", " "); // заменить все множественные табуляции одним пробелом
            var spl = text.Split(' '); // делим строку на слова
            var ptr = spl.Length - 1;
            var varname = spl[ptr--]; // последнее - это идентификатор
            varname = varname.Replace(";", "");
            res += varname + " as a ";
            bool const_decl, pointer_decl;

            while (ptr >= 0)
            {
                if (spl[ptr].Contains("*"))
                {
                    res += "pointer to ";
                    if (Replace(ref spl[ptr], "*", ""))
                        ptr--;
                    continue;
                }
                if (spl[ptr].Contains("&"))
                {
                    res += "reference to ";
                    if (Replace(ref spl[ptr], "&", ""))
                        ptr--;
                    continue;
                }
                if (spl[ptr].Contains("const"))
                {
                    res += "constant ";
                    Replace(ref spl[ptr], "const", "");
                    ptr--;
                    if (ptr >= 0)
                    {
                        if (spl[ptr].Contains("*"))
                        {
                            res += "pointer to ";
                            Replace(ref spl[ptr], "*", "");
                            continue;
                        }
                        if (spl[ptr].Contains("&"))
                        {
                            res += "reference to ";
                            Replace(ref spl[ptr], "&", "");
                            continue;
                        }
                        if (spl[ptr] != "")
                        res += spl[ptr] + " ";
                        spl[ptr] = "";
                    }
                    ptr--;
                }
                else
                {
                    if (ptr - 1 >= 0)
                    {
                        if (spl[ptr - 1].Contains("const"))
                        {
                            res += "constant ";
                            Replace(ref spl[ptr - 1], "const", "");
                            if (spl[ptr].Contains("*"))
                            {
                                res += "pointer to ";
                                Replace(ref spl[ptr], "*", "");
                                ptr--;
                                continue;
                            }
                            if (spl[ptr].Contains("&"))
                            {
                                res += "reference to ";
                                Replace(ref spl[ptr], "&", "");
                                ptr--;
                                continue;
                            }
                            res += spl[ptr] + " ";
                            if (spl[ptr] != "")
                                spl[ptr] = "";
                            ptr--;
                        }
                        else
                        {
                            res += spl[ptr] + " ";
                            spl[ptr] = "";
                            ptr--;
                        }
                    }
                    else
                    {
                        res += spl[ptr];
                        spl[ptr] = "";
                        ptr--;
                    }

                }
            }
            return Regex.Replace(res, @"(\s)\s+", " ");
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DTE dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
            TextSelection ts = (TextSelection)dte.ActiveDocument.Selection;
            string title = ts.Text;
            // Show a message box to prove we were here
            string message = Translate(ts.Text);
            if (message != "")
            {
                IVsStatusbar statusBar = (IVsStatusbar)Package.GetGlobalService(typeof(SVsStatusbar));
                int frozen;
                statusBar.IsFrozen(out frozen);
                if (frozen != 0)
                    statusBar.FreezeOutput(0);
                statusBar.SetText(message);
                statusBar.FreezeOutput(1);
            }
        }

    }
}
