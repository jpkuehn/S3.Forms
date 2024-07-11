using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Services.Notifications;

namespace S3.Forms.Handlers {
    /// <summary>
    /// Catch form submissions before being saved and perform custom validation.
    /// See https://docs.umbraco.com/umbraco-forms/developer/extending/adding-an-event-handler
    /// </summary>
    public class FormValidateNotificationHandler : INotificationHandler<FormValidateNotification> {

        public void Handle(FormValidateNotification notification) {
            // if needed, be selective about which form submissions you affect...
            //Form form = notification.Form;
            //if (form.Id.Equals("66f2a003-e8da-4fed-ab98-308cab611ad6")) {
            //if (form.Name == "Contact Form") {
                var tempDataFactory = StaticServiceProvider.Instance.GetRequiredService<ITempDataDictionaryFactory>();
                var tempData = tempDataFactory.GetTempData(notification.Context);

                tempData["form-error-field-ids"] = string.Empty;

                // check the ModelState
                if (notification.ModelState.IsValid == false) {
                    List<string> errorFields = new List<string>();
                    // get form field id from modelstate key
                    foreach (KeyValuePair<string, ModelStateEntry> kvp in notification.ModelState) {
                        ModelStateEntry value = kvp.Value;
                        if (value != null) {
                            if (value.ValidationState == ModelValidationState.Invalid) {
                                string key = kvp.Key;
                                if (key != null) {
                                    errorFields.Add(key);
                                }
                            }
                        }
                    }

                    if (errorFields.Count > 0) {
                        tempData["form-error-field-ids"] = errorFields;
                    }
                }
            //}
        }

    }
}
