using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.Extensions;
using Umbraco.Forms.Core;
using Umbraco.Forms.Core.Attributes;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using Umbraco.Forms.Web.Models.Backoffice;
using Newtonsoft.Json;
using S3.Forms.Models;
using Umbraco.Forms.Core.Providers.Models;

///<summary>
/// Purpose: For forms that should not store sensitive (personally identifiable) data, insert tracking data only. If no form fields 
///          are selected, the timestamp, Id's and IP address will be saved in the db.
/// 
/// 1. To ensure form data is not saved to db, uncheck Store Records...
///    Umbraco admin UI -> Forms tab -> Forms folder -> select form -> Settings -> Store Records
/// 2. Each workflow's "Include Sensitive Data" only prevents non-admins from seeing data that is stored. This setting has no impact on saving data. 
///    If Store Records is checked and Include Sensitive Data is unchecked, all data is still stored in db.
/// 3. Enter tracking fields as semi-colon delimited string in the workflow's Tracking Fields. Only form fields will be saved.
/// 4. Info for built-in setting types can be found at https://docs.umbraco.com/umbraco-forms/v/13.forms.latest-lts/developer/extending/adding-a-type/setting-types
///</summary>

namespace S3.Forms.Workflows {
    public class InsertTrackingData : WorkflowType {
        private readonly ILogger<InsertTrackingData> _logger;
        private readonly IRecordStorage _recordStorage;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITempDataDictionaryFactory _tempDataDictionaryFactory;
        private readonly IWorkflowHelper _workflowHelper;

        // Notes:
        // 1. gets or sets the form fields that will be saved in the database for tracking purposes
        // 2. based off FieldMapper (built-in Setting Type)
        //    - Umbraco.Forms.StaticAssets/staticassets/backoffice/common/settingtypes/fieldmapper.html
        //    
        [Setting("Tracking Fields", Alias = "TrackingFields", Description = "Select the form fields that should be saved to database.", SupportsPlaceholders = true, View = "~/_content/S3.Forms.UI.Resources/App_Plugins/FormsExtensions/backoffice/SettingTypes/trackingfields.html", DisplayOrder = 10)]
        public virtual string TrackingFields { get; set; } = string.Empty;

        public InsertTrackingData(
            ILogger<InsertTrackingData> logger,
            IRecordStorage recordStorage,
            IHttpContextAccessor httpContextAccessor,
            ITempDataDictionaryFactory tempDataDictionaryFactory,
            IWorkflowHelper workflowHelper
            ) {
            _logger = logger;
            _recordStorage = recordStorage;
            _httpContextAccessor = httpContextAccessor;
            _tempDataDictionaryFactory = tempDataDictionaryFactory;
            _workflowHelper = workflowHelper;

            this.Id = new Guid("033F8673-B6DF-46B0-909D-9B5C252ACF9A");
            this.Name = "Insert tracking data";
            this.Alias = "insertTrackingData";
            this.Description = "Insert tracking data into database";
            this.Icon = "icon-server-alt";
            this.Group = "Email";
        }

        public override Task<WorkflowExecutionStatus> ExecuteAsync(WorkflowExecutionContext context) {
            Form? form = context.Form;
            if (form != null) {
                try {
                    string ipAddr = context.Record.IP;

                    // search form fields (context.Form.AllFields) and match against tracking fields values. store in dictionary.
                    Dictionary<Guid, RecordField> recordFieldsToBeStored = new Dictionary<Guid, RecordField>();

                    if (!string.IsNullOrEmpty(TrackingFields)) {
                        // tracking field guid is value in dropdown control.
                        // TrackingFields string looks like:
                        //   {\"value\":\"4881f502-be1a-4a0c-89b1-82d364c37620\",\"$$hashKey\":\"object:1073\"},
                        //   {\"value\":\"2774ead9-784c-47a5-aebb-ce9169e765f9\",\"$$hashKey\":\"object:1084\"}
                        List<TrackingField> trackingFields = JsonConvert.DeserializeObject<List<TrackingField>>(TrackingFields);

                        if (trackingFields != null) {
                            foreach (Field field in form.AllFields) {
                                // is the field in the list of tracking fields?
                                if (trackingFields.Contains(new TrackingField { Value = field.Id.ToString()})) {
                                    List<object> fieldValues = GetFieldValues(field);
                                    RecordField recordField = new RecordField(field);
                                    // if list of values has any elements, add it to dictionary
                                    if (fieldValues.Any()) {
                                        recordField.Values = fieldValues;
                                    }
                                    recordFieldsToBeStored.Add(field.Id, recordField);
                                }
                                else {
                                    // if not tracking field, add field as blank/empty because... if admin switches
                                    // UI from save data to not saving data, it causes the Entries page to hang.
                                    //
                                    // some fields, such as recaptcha, don't have values. skip them altogether
                                    if (field.Values.Any()) {
                                        RecordField recordField = new RecordField(field);
                                        recordFieldsToBeStored.Add(field.Id, recordField);
                                    }
                                }
                            }
                        }
                    }

                    // 1. UmbracoPageId and MemberKey don't appear to be used at this point
                    // 2. Need to manually insert UniqueId and Id to match model in email template
                    // Reference: https://www.andybutland.dev/2023/04/blog-post.html
                    Record record = new Record() {
                        Created = DateTime.Now,
                        Culture = context.Record.Culture,
                        CurrentPage = context.Record.CurrentPage,
                        Form = form.Id,
                        IP = ipAddr,
                        MemberKey = context.Record.MemberKey,
                        RecordFields = recordFieldsToBeStored,
                        State = FormState.Submitted,
                        UmbracoPageId = context.Record.UmbracoPageId,
                        UniqueId = context.Record.UniqueId
                    };

                    record.RecordData = record.GenerateRecordDataAsJson();
                    _recordStorage.InsertRecord(record, form);
                    _recordStorage.DisposeIfDisposable();

                    // add record.Id to TempData here. retrieve in email template
                    var httpContext = _httpContextAccessor.HttpContext;
                    var tempData = _tempDataDictionaryFactory.GetTempData(httpContext);
                    tempData["S3_Forms_Submitted_Record_Id"] = record.Id.ToString();
                    tempData["S3_Forms_Submitted_IP_Addr"] = ipAddr;

                    _workflowHelper.InsertWorkflowAuditTrailRecord(record, form, FormState.Submitted, this.Workflow, this.Id);

                    return Task.FromResult(WorkflowExecutionStatus.Completed);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, $"{(object)this.Workflow.Name}: Failure inserting tracking data for form = {form.Name}, unique ID {(object)form.Id}, record ID = {context.Record.UniqueId}");
                    return Task.FromResult(WorkflowExecutionStatus.Failed);
                }
            }
            else {
                _logger.LogWarning($"{(object)this.Workflow.Name}: Form does not exist for record ID = {context.Record.UniqueId}");
                return Task.FromResult(WorkflowExecutionStatus.Failed);
            }
        }

        private List<object> GetFieldValues(Field f) {
            List<object> fieldValues = new List<object>();
            foreach(object obj in f.Values) {
                if (obj != null) {
                    if ((string)obj != string.Empty) {
                        fieldValues.Add(obj);
                    }
                }
            }
            return fieldValues;
        }

        public override List<Exception> ValidateSettings() {
            return new List<Exception>();
        }

    }
}
