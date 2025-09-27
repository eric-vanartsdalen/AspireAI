using AspireApp.Web.Components.Pages;
using Microsoft.AspNetCore.Components;

namespace AspireApp.Web.Components.Pages
{
    public partial class Home : ComponentBase
    {
        protected override void OnInitialized()
        {
            // Configuration is already called in Program.cs, but ensure it's done
            HomeConfigurations.PullConfigure();
        }
        
        protected void RefreshValues()
        {
            // Force reconfiguration when explicitly requested
            HomeConfigurations.ForceReconfigure();
            StateHasChanged();
        }
    }
}
