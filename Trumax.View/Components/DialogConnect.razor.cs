using Maanfee.Web.Core;
using MudBlazor;
using System.Net.Http.Json;
using Trumax.View.ViewModels;

namespace Trumax.View.Components
{
    public partial class DialogConnect
    {
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                var PostResult = await Http!.PostAsJsonAsync("api/SqlServerdbManager/TestConnection", ViewModel);
                if (PostResult.IsSuccessStatusCode)
                {
                    var JsonResult = await PostResult.Content.ReadFromJsonAsync<CallbackResult<string>>();
                    if (JsonResult?.Data != null)
                    {
                        Snackbar!.Add($"{JsonResult.Data}", Severity.Success);
                    }
                    else
                    {
                        Snackbar!.Add(JsonResult!.Error!.Message!, Severity.Error);
                        // Snackbar!.Add(MessageHandler.ErrorHandler(JsonResult!.Error!), Severity.Error);
                    }
                }
                else
                {
                    Snackbar!.Add(PostResult.Content.ReadAsStringAsync().Result, Severity.Error);
                }
            }
            catch (Exception? ex)
            {
                Snackbar!.Add(ex.Message, Severity.Error);
            }
            finally
            {

            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                var PostResult = await Http!.PostAsJsonAsync("api/SqlServerdbManager/TestConnection", ViewModel);
                if (PostResult.IsSuccessStatusCode)
                {
                    var JsonResult = await PostResult.Content.ReadFromJsonAsync<CallbackResult<string>>();
                    if (JsonResult?.Data != null)
                    {
                        MudDialog!.Close(DialogResult.Ok(ViewModel));
                    }
                    else
                    {
                        Snackbar!.Add(JsonResult!.Error!.Message!, Severity.Error);
                        // Snackbar!.Add(MessageHandler.ErrorHandler(JsonResult!.Error!), Severity.Error);
                    }
                }
                else
                {
                    Snackbar!.Add(PostResult.Content.ReadAsStringAsync().Result, Severity.Error);
                }
            }
            catch (Exception? ex)
            {
                Snackbar!.Add(ex.Message, Severity.Error);
            }
            finally
            {

            }
        }

        // *****************************************************

        private readonly Login ViewModel = new();

        private bool PasswordVisibility;
        private InputType PasswordInput = InputType.Password;
        private string PasswordInputIcon = Icons.Material.Filled.VisibilityOff;

        private void TogglePasswordVisibility()
        {
            if (PasswordVisibility)
            {
                PasswordVisibility = false;
                PasswordInputIcon = Icons.Material.Filled.VisibilityOff;
                PasswordInput = InputType.Password;
            }
            else
            {
                PasswordVisibility = true;
                PasswordInputIcon = Icons.Material.Filled.Visibility;
                PasswordInput = InputType.Text;
            }
        }

    }
}
