using Microsoft.Extensions.DependencyInjection;
using S3.Forms.Handlers;
using S3.Forms.Workflows;
using S3.Utilities.SecureEmail;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Forms.Core.Providers.Extensions;
using Umbraco.Forms.Core.Services.Notifications;

namespace S3.Forms.Startup {
    public class S3FormsComposer : IComposer {

        public void Compose(IUmbracoBuilder builder) {
            builder.AddFormsWorkflow<SendSecureEmail>();
            builder.AddFormsWorkflow<InsertTrackingData>();
            builder.Services.AddSingleton<IWorkflowHelper, WorkflowHelper>();

            // https://docs.umbraco.com/umbraco-forms/developer/extending/adding-an-event-handler
            builder.AddNotificationHandler<FormValidateNotification, FormValidateNotificationHandler>();
        }

    }
}
