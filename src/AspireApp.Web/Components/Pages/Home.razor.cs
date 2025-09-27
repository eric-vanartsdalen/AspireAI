using AspireApp.Web.Components.Pages;
using Microsoft.AspNetCore.Components;

namespace AspireApp.Web.Components.Pages
{
    public partial class Home : ComponentBase
    {
        protected override void OnInitialized()
        {
            // Calling configuration setup during initialization
            HomeConfigurations.PullConfigure();
        }
        
        // Add OnParametersSet as well to handle potential re-rendering
        protected override void OnParametersSet()
        {
            HomeConfigurations.PullConfigure();
            base.OnParametersSet();
        }

        // Force refresh when navigating to this component
        protected override void OnAfterRender(bool firstRender)
        {
            if (firstRender)
            {
                HomeConfigurations.PullConfigure();
                StateHasChanged();
            }
        }

        protected void RefreshValues()
        {
            HomeConfigurations.PullConfigure();
            StateHasChanged();
        }
    }
}
