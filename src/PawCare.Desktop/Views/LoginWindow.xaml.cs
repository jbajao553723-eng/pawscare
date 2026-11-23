using System.Windows;
using System.IO;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;
using PawCare.Desktop.Controls;

namespace PawCare.Desktop.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        var signInButton = (System.Windows.Controls.Button)sender;
        var username = UsernameTextBox.Text;
        var password = PasswordInput.Password;
        signInButton.IsEnabled = false;
        ErrorText.Text = "Locating your workspace...";
        try
        {
            var session = await Task.Run(() => App.AuthenticateRouted(username, password));
            if (session is null)
            {
                ErrorText.Text = "The username or password is incorrect, or the account is inactive.";
                return;
            }

            BranchRecord? branch = null;
            if (session.Role != UserRole.SuperAdmin)
            {
                var branches = await Task.Run(() => App.MasterDatabase.GetBranches(session.TenantId).Where(x => x.IsActive).ToArray());
                if (branches.Length == 0) { ErrorText.Text = "This organization has no active branch."; return; }
                if (branches.Length > 1)
                {
                    var choices = branches.ToDictionary(x => $"{x.Code} · {x.Name}", x => x);
                    var dialog = new RecordDialog(this, "Choose branch", "Select the branch database for this session.",
                        [new("branch", "Branch", FieldKind.Choice, choices.Keys.First(), choices.Keys.ToArray())], "Continue");
                    if (dialog.ShowModal() != true) return;
                    branch = choices[dialog.Text("branch")];
                }
                else branch = branches[0];
            }
            ErrorText.Text = "Opening workspace...";
            session = await Task.Run(() => App.ActivateSession(session, branch));
            new MainWindow(session).Show();
            Close();
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or UnauthorizedAccessException or IOException)
        { ErrorText.Text = $"Could not open the selected workspace: {exception.Message}"; }
        finally
        {
            signInButton.IsEnabled = true;
        }
    }
}
