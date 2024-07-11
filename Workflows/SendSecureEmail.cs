using System.Reflection;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using S3.Utilities.SecureEmail;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;
using Umbraco.Forms.Core;
using Umbraco.Forms.Core.Attributes;
using Umbraco.Forms.Core.Controllers;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using Umbraco.Forms.Core.Providers.WorkflowTypes;
using Umbraco.Forms.Core.Services;
using Umbraco.Forms.Web.Extensions;
using static Umbraco.Forms.Core.Constants;

namespace S3.Forms.Workflows {
    public class SendSecureEmail : BaseSecureEmailWorkflowType {

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFieldTypeStorage _fieldTypeStorage;
        private readonly IFormService _formService;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IPageService _pageService;
        private readonly MediaFileManager _mediaFileManager;
        private readonly FileSystems _fileSystems;
        private readonly IPublishedUrlProvider _publishedUrlProvider;
        private readonly ILogger<SendSecureEmail> _logger;
        private readonly IPlaceholderParsingService _placeholderParsingService;
        private readonly IPrevalueSourceService _prevalueSourceService;
        private readonly IFieldPreValueSourceTypeService _fieldPreValueSourceTypeService;
        private readonly IMimeSmtpService _mimeSmtpService;
        private readonly IWorkflowHelper _workflowHelper;

        // Summary: gets or sets a path to the razor view used for generating the email.
        [Setting("Email Template", Description = "The path to the Razor view that you want to use for generating the email. Email templates are stored at /Views/Partials/Forms/Emails. Required", View = "EmailTemplatePicker", IsMandatory = true, DisplayOrder = 110)]
        public virtual string RazorViewFilePath { get; set; } = string.Empty;

        // Summary: gets or sets the form field's formatted text to be used in the header of the email.
        [Setting("Header text", Description = "Enter formatted text to be rendered in the email header.", SupportsPlaceholders = true, HtmlEncodeReplacedPlaceholderValues = true, View = "RichText", DisplayOrder = 120)]
        public virtual string HeaderHtml { get; set; } = string.Empty;

        // Summary: gets or sets the form field's formatted text to be used in the header of the email.
        [Setting("Footer text", Description = "Enter formatted text to be rendered in the email footer.", SupportsPlaceholders = true, HtmlEncodeReplacedPlaceholderValues = true, View = "RichText", DisplayOrder = 130)]
        public virtual string FooterHtml { get; set; } = string.Empty;

        // Summary: gets or sets a value indicating whether to attach file uploads to the email.
        [Setting("Attach Uploaded Files", Description = "Select to attach file uploads to email.", View = "Checkbox", DisplayOrder = 140)]
        public virtual string HasAttachments { get; set; } = string.Empty;

        // Summary: gets or sets a value indicating whether to add aria-describedby to the error fields
        [Setting("Show 'aria-describedby' in input field?", Description = "Select for 'aria-describedby' to be added to input fields upon validation error. The value will be set to the corresponding error field's ID.", View = "Checkbox", DisplayOrder = 150)]
        public virtual string SetAriaDescribedBy { get; set; } = string.Empty;

        // Summary: gets or sets whether to set focus to the first errored field
        [Setting("Set focus for first error field?", Description = "Select to set focus to first error field in the form.", View = "Checkbox", DisplayOrder = 160)]
        public virtual string SetFirstErrorFocus { get; set; } = string.Empty;

        public SendSecureEmail(
            IHostingEnvironment hostingEnvironment,
            IOptions<GlobalSettings> globalSettings,
            IHttpContextAccessor httpContextAccessor,
            IFieldTypeStorage fieldTypeStorage,
            IFormService formService,
            IUmbracoContextAccessor umbracoContextAccessor,
            IPageService pageService,
            MediaFileManager mediaFileManager,
            FileSystems fileSystems,
            IPublishedUrlProvider publishedUrlProvider,
            ILogger<SendSecureEmail> logger,
            IPlaceholderParsingService placeholderParsingService,
            IPrevalueSourceService prevalueSourceService,
            IFieldPreValueSourceTypeService fieldPreValueSourceTypeService,
            IMimeSmtpService mimeSmtpService,
            IWorkflowHelper workflowHelper)
            : base(hostingEnvironment, globalSettings, mediaFileManager, logger) {
            _httpContextAccessor = httpContextAccessor;
            _fieldTypeStorage = fieldTypeStorage;
            _formService = formService;
            _umbracoContextAccessor = umbracoContextAccessor;
            _pageService = pageService;
            _mediaFileManager = mediaFileManager;
            _fileSystems = fileSystems;
            _publishedUrlProvider = publishedUrlProvider;
            _logger = logger;
            _placeholderParsingService = placeholderParsingService;
            _prevalueSourceService = prevalueSourceService;
            _fieldPreValueSourceTypeService = fieldPreValueSourceTypeService;
            _mimeSmtpService = mimeSmtpService;
            _workflowHelper = workflowHelper;

            base.Id = new Guid("E6DC2D5C-863F-4138-8913-92CE3BB97DE6");
            base.Name = "Send encrypted email with template (Razor)";
            base.Alias = "sendEncryptedEmailWithRazorTemplate";
            base.Description = "Send the result of the form as an encrypted email to an email address/addresses using a Razor .cshtml template";
            base.Icon = "icon-lock";
            base.Group = "Email";
        }

        public override List<Exception> ValidateSettings() {
            List<Exception> list = new List<Exception>();

            if (string.IsNullOrWhiteSpace(RazorViewFilePath)) {
                list.Add(new ArgumentNullException("RazorViewFilePath", "'Email Template' setting is empty.'"));
            }

            return list;
        }

        public override async Task<WorkflowExecutionStatus> ExecuteAsync(WorkflowExecutionContext context) {
            if (context.Record == null) {
                ArgumentNullException argumentNullException = new ArgumentNullException("record");
                _logger.LogError(argumentNullException, "Record is null");
                return WorkflowExecutionStatus.Failed;
            }

            List<Exception> list = ValidateSettings();
            if (list != null && list.Any()) {
                foreach (Exception exception in list) {
                    _logger.LogError(exception, exception.Message);
                }

                return WorkflowExecutionStatus.Failed;
            }

            if (_fileSystems.PartialViewsFileSystem == null) {
                _logger.LogError("Partial view file system is null.");
                return WorkflowExecutionStatus.Failed;
            }

            if (!Umbraco.Forms.Core.Constants.EmailTemplates.CoreEmailTemplates.Any((string x) => "Forms/Emails/" + x == RazorViewFilePath) && !_fileSystems.PartialViewsFileSystem.FileExists(RazorViewFilePath)) {
                FileNotFoundException notFoundException = new FileNotFoundException("Razor view email template not found", RazorViewFilePath);
                _logger.LogError(notFoundException, "Razor view email template not found {RazorViewFilePath}", RazorViewFilePath);
                return WorkflowExecutionStatus.Failed;
            }

            Form? form = context.Form;
            if (form != null) {
                try {
                    string razorViewBody = ParseWithRazorView(context.Record, "/Views/Partials/" + RazorViewFilePath);

                    List<MimePart> attachments = new List<MimePart>();
                    if (HasAttachments == true.ToString()) {
                        IList<Guid> uploadFieldTypeIds = GetUploadFieldTypeIdsUsedInRecord(context.Record);
                        if (uploadFieldTypeIds.Count > 0) {
                            foreach (KeyValuePair<Guid, RecordField> keyValuePair in context.Record.RecordFields.Where<KeyValuePair<Guid, RecordField>>((KeyValuePair<Guid, RecordField> x) => uploadFieldTypeIds.Contains(x.Value.Field.FieldTypeId))) {
                                if (!keyValuePair.Value.HasValue()) {
                                    continue;
                                }

                                foreach (object value in keyValuePair.Value.Values) {
                                    // path is relative
                                    string attachmentPath = value.ToString() ?? string.Empty;
                                    if (!string.IsNullOrEmpty(attachmentPath)) {
                                        // create an attachment for the file located at path
                                        MimePart? attachment;
                                        if (TryCreateAttachment(_mediaFileManager.FileSystem, attachmentPath, out attachment)) {
                                            attachments.Add(attachment);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (_mimeSmtpService.CanSendRequiredEmail()) {
                        MimeMessage mm = await _mimeSmtpService.AssembleMessage(new SecureEmailArgs() {
                            RecipientEmail = base.Email,
                            CcEmail = base.CcEmail,
                            BccEmail = base.BccEmail,
                            BccFormsInbox = Convert.ToBoolean(base.BccFormsInbox),
                            FromEmail = base.FromEmail,
                            SenderEmail = base.SenderEmail,
                            ReplyToEmail = base.ReplyToEmail,
                            Subject = base.Subject,
                            Body = razorViewBody,
                            Attachments = attachments,
                            SignEmail = Convert.ToBoolean(base.SignEmail)
                        });

                        if (mm != null) {
                            await _mimeSmtpService.SendAsync(mm, "S3.SendSecureFormWorkflow");

                            // dispose of stream... https://github.com/jstedfast/MimeKit/issues/570
                            foreach (var part in mm.BodyParts.OfType<MimePart>()) {
                                part.Content?.Stream.Dispose();
                            }

                            // must call this
                            TryCleanUpAttachments(form.Name, form.Id.ToString(), context.Record.UniqueId.ToString());

                            //RecordFields = recordFieldsToBeStored,

                            Record record = new Record() {
                                Created = DateTime.Now,
                                Culture = context.Record.Culture,
                                CurrentPage = context.Record.CurrentPage,
                                Form = form.Id,
                                IP = context.Record.IP,
                                MemberKey = context.Record.MemberKey,
                                State = FormState.Submitted,
                                UmbracoPageId = context.Record.UmbracoPageId,
                                UniqueId = context.Record.UniqueId
                            };

                            _workflowHelper.InsertWorkflowAuditTrailRecord(record, form, FormState.Submitted, this.Workflow, this.Id);

                            return WorkflowExecutionStatus.Completed;
                        }
                        else {
                            string msg = $"{(object)this.Workflow.Name}: SendSecureEmail failed to send message. MimeMessage is null.";
                            _logger.LogError(msg);
                            return WorkflowExecutionStatus.Failed;
                        }
                    }
                    else {
                        string msg = $"{(object)this.Workflow.Name}: Core email service reports that email message cannot be sent.";
                        _logger.LogError(msg);
                        throw new InvalidOperationException(msg);
                    }
                }
                catch (Exception ex) {
                    _logger.LogError(ex, $"{(object)this.Workflow.Name}: Failure sending a Razor email to {Email} for Form = {form.Name}, unique ID = {(object)form.Id}, record ID {context.Record.UniqueId}");
                    return WorkflowExecutionStatus.Failed;
                }
            }
            else {
                _logger.LogWarning($"{(object)this.Workflow.Name}: Form does not exist for record ID = {context.Record.UniqueId}");
                return WorkflowExecutionStatus.Failed;
            }
        }

        private IList<Guid> GetUploadFieldTypeIdsUsedInRecord(Record record) {
            return (from x in record.RecordFields
                    select x.Value.Field into x
                    select _fieldTypeStorage.GetFieldTypeByField(x) into x
                    where x?.SupportsUploadTypes ?? false
                    select x.Id).ToList();
        }

        // Summary: Parses a record with a Razor view to generate an HTML output to use in for instance an email
        // Parameters: 
        //   record: The record
        //   razorViewFilePath: A relative path to a Razor view
        // Returns: The generated output string usually HTML
        private string ParseWithRazorView(Record record, string razorViewFilePath) {
            if (string.IsNullOrWhiteSpace(razorViewFilePath)) {
                ArgumentNullException argumentNullException = new ArgumentNullException("razorViewFilePath");
                _logger.LogError(argumentNullException, "RazorFilePath cannot be null or empty");
                throw argumentNullException;
            }

            if (record.RecordFields.Values.Any((RecordField x) => string.IsNullOrWhiteSpace(x.Alias))) {
                string message = "An alias without a value was in the recordfields. This should be fixed.";
                InvalidOperationException operationException = new InvalidOperationException(message);
                _logger.LogError(operationException, message);
                throw operationException;
            }

            if (_httpContextAccessor.HttpContext == null || !_umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext? umbracoContext)) {
                string message2 = "Context is not available to parse the record";
                InvalidOperationException invalidOperationException = new InvalidOperationException(message2);
                _logger.LogError(invalidOperationException, message2);
                throw invalidOperationException;
            }

            _httpContextAccessor.HttpContext.Items["pageElements"] = _pageService.GetPageElements(record.UmbracoPageId);
            FormFieldHtmlModel[] fields = record.RecordFields.Select<KeyValuePair<Guid, RecordField>, FormFieldHtmlModel>((KeyValuePair<Guid, RecordField> recordField) => new FormFieldHtmlModel(recordField.Value.Alias) {
                Id = (recordField.Value?.FieldId ?? Guid.Empty),
                Name = _placeholderParsingService.ParsePlaceHolders(recordField.Value?.Field?.Caption ?? string.Empty, htmlEncodeValues: false),
                FieldType = ((recordField.Value?.Field == null) ? string.Empty : (_fieldTypeStorage.GetFieldTypeByField(recordField.Value.Field)?.FieldTypeViewName ?? string.Empty)),
                FieldValue = ((recordField.Value != null && recordField.Value.HasValue()) ? recordField.Value.Values.ToArray() : new object[1] { string.Empty })
            }).ToArray();

            HttpContext? httpContext = _httpContextAccessor.HttpContext;
            FormsHtmlModel model = CreateModelForTemplate(record, fields);
            ActionContext context = new ActionContext(httpContext, new RouteData {
                Values =
                {
                {
                    "controller",
                    (object?)"RazorEmailView"
                },
                {
                    "action",
                    (object?)"Index"
                }
            }
            }, new ControllerActionDescriptor());
            RazorEmailViewController controller = new RazorEmailViewController {
                ControllerContext = new ControllerContext(context)
            };
            try {
                bool inPreviewMode = umbracoContext.InPreviewMode;
                umbracoContext.ForcedPreview(preview: false);
                string result = controller.RenderViewAsync(razorViewFilePath, model).GetAwaiter().GetResult();
                umbracoContext.ForcedPreview(inPreviewMode);
                return result;
            }
            catch (Exception exception) {
                _logger.LogError(exception, "Error parsing record with Razor view - {RazorViewFilePath}", razorViewFilePath);
                throw;
            }
        }

        private FormsHtmlModel CreateModelForTemplate(Record record, FormFieldHtmlModel[] fields) {
            FormsHtmlModel formsHtmlModel = new FormsHtmlModel(fields) {
                FormId = record.Form,
                EntryUniqueId = record.UniqueId,
                FormSubmittedOn = record.Created
            };
            Form? form = _formService.Get(record.Form);
            if (form != null) {
                formsHtmlModel.FormName = form.Name;
                //formsHtmlModel.PrevalueMaps = form.GetPrevalueMaps(_fieldTypeStorage, _prevalueSourceService, _fieldPreValueSourceTypeService);
                formsHtmlModel.PrevalueMaps = GetPrevalueMaps(form, _fieldTypeStorage, _prevalueSourceService, _fieldPreValueSourceTypeService);
            }

            formsHtmlModel.HeaderHtml = GetHtmlContent(HeaderHtml);
            formsHtmlModel.FooterHtml = GetHtmlContent(FooterHtml);
            formsHtmlModel.WorkflowSettings = GetWorflowSettings();
            if (record.UmbracoPageId > 0) {
                formsHtmlModel.FormPageId = record.UmbracoPageId;
                formsHtmlModel.FormPageUrl = _publishedUrlProvider.GetUrl(record.UmbracoPageId, UrlMode.Absolute);
            }

            return formsHtmlModel;
        }

        private IHtmlContent? GetHtmlContent(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return null;
            }

            return new HtmlString(value);
        }

        private IDictionary<string, string> GetWorflowSettings() {
            return (from x in GetType().GetProperties()
                    where Attribute.IsDefined(x, typeof(SettingAttribute))
                    select x).ToDictionary((PropertyInfo x) => x.Name, (PropertyInfo x) => (x.GetValue(this) as string) ?? string.Empty);
        }

        private Dictionary<Guid, Dictionary<string, string?>> GetPrevalueMaps(Form form, IFieldTypeStorage fieldTypeStorage, IPrevalueSourceService prevalueSourceService, IFieldPreValueSourceTypeService fieldPreValueSourceTypeService) {
            Dictionary<Guid, Dictionary<string, string?>> dictionary = new Dictionary<Guid, Dictionary<string, string?>>();
            if (form == null) {
                return dictionary;
            }

            foreach (Field allField in form.AllFields) {
                FieldType? fieldTypeByField = fieldTypeStorage.GetFieldTypeByField(allField);
                if (fieldTypeByField != null && fieldTypeByField.SupportsPreValues) {
                    Dictionary<string, string?> value = (from x in GetPrevaluesForFormField(allField, form, prevalueSourceService, fieldPreValueSourceTypeService)
                                                         group x by x.Value into x
                                                         select x.First()).ToDictionary((PreValue x) => x.Value, (PreValue x) => x.Caption);
                    dictionary.Add(allField.Id, value);
                }
            }

            return dictionary;
        }

        private IList<PreValue> GetPrevaluesForFormField(Field formField, Form form, IPrevalueSourceService prevalueSourceService, IFieldPreValueSourceTypeService fieldPreValueSourceTypeService) {
            List<PreValue> result = new List<PreValue>();
            if (formField.PreValueSourceId != Guid.Empty && prevalueSourceService != null) {
                FieldPreValueSource? fieldPreValueSource = prevalueSourceService.Get(formField.PreValueSourceId);
                if (fieldPreValueSource != null && fieldPreValueSourceTypeService != null) {
                    FieldPreValueSourceType? byId = fieldPreValueSourceTypeService.GetById(fieldPreValueSource.FieldPreValueSourceTypeId);
                    if (byId != null) {
                        byId.LoadSettings(fieldPreValueSource);
                        result = byId.GetPreValues(formField, form);
                    }
                }
            }
            else {
                result = formField.PreValues.Select((FieldPrevalue x) => new PreValue {
                    Value = x.Value,
                    Caption = x.Caption
                }).ToList();
            }

            return result;
        }

    }
}