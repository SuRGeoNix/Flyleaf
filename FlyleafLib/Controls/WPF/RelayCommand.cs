using System;
using System.Windows.Input;

namespace FlyleafLib.Controls.WPF
{
    public class RelayCommand : ICommand
    {
        private Action<object> execute;

        private Predicate<object> canExecute;

        private event EventHandler CanExecuteChangedInternal;

        public RelayCommand(Action<object> execute) : this(execute, DefaultCanExecute) { }

        public RelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            if (canExecute == null)
            {
                throw new ArgumentNullException("canExecute");
            }

            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add
            {
                CommandManager.RequerySuggested += value;
                CanExecuteChangedInternal += value;
            }

            remove
            {
                CommandManager.RequerySuggested -= value;
                CanExecuteChangedInternal -= value;
            }
        }

        private static bool DefaultCanExecute(object parameter) { return true; }
        public bool CanExecute(object parameter) { return canExecute != null && canExecute(parameter); }

        public void Execute(object parameter) { execute(parameter); }

        public void OnCanExecuteChanged()
        {
            EventHandler handler = CanExecuteChangedInternal;
            handler?.Invoke(this, EventArgs.Empty);
             //CommandManager.InvalidateRequerySuggested();
        }

        public void Destroy()
        {
            canExecute = _ => false;
            execute = _ => { return; };
        }
    }
}
