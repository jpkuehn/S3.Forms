using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Diagnostics.CodeAnalysis;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.IO;
using Umbraco.Forms.Core;
using Umbraco.Forms.Core.Attributes;

namespace S3.Forms.Workflows {

    // Summary:
    //     Provides common functionality for a Umbraco.Forms.Core.WorkflowType to send the
    //     contents of a Umbraco.Forms.Core.Persistence.Dtos.Record as an email notification.
    public abstract class BaseSecureEmailWorkflowType : WorkflowType {
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly GlobalSettings _globalSettings;
        private readonly MediaFileManager _mediaFileManager;
        private readonly ILogger<BaseSecureEmailWorkflowType> _logger;

        // contains relative file paths. starts with /forms/uploads/...
        private List<string> absolutePaths = new List<string>();
        private List<Stream> streams = new List<Stream>();

        protected BaseSecureEmailWorkflowType(
          IHostingEnvironment hostingEnvironment,
          IOptions<GlobalSettings> globalSettings,
          MediaFileManager mediaFileManager,
          ILogger<BaseSecureEmailWorkflowType> logger
          ) {
            _hostingEnvironment = hostingEnvironment;
            _globalSettings = globalSettings.Value;
            _mediaFileManager = mediaFileManager;
            _logger = logger;
        }

        /// <summary>Gets or sets the receiver email address(es).</summary>
        [Setting("Recipient Email", Description = "Enter the recipient(s) email addresses. Semi-colon or comma delimited if more than one. Required.", View = "TextField", SupportsPlaceholders = true, IsMandatory = true, DisplayOrder = 10)]
        public virtual string Email { get; set; } = string.Empty;

        /// <summary>Gets or sets the receiver (CC) email address(es).</summary>
        [Setting("CC Email", Description = "Enter the CC recipient(s) email addresses. Semi-colon or comma delimited if more than one.", View = "TextField", SupportsPlaceholders = true, DisplayOrder = 20)]
        public virtual string CcEmail { get; set; } = string.Empty;

        /// <summary>Gets or sets the receiver (BCC) email address(es).</summary>
        [Setting("BCC Email", Description = "Enter the BCC recipient(s) email addresses. Semi-colon or comma delimited if more than one.", View = "TextField", SupportsPlaceholders = true, DisplayOrder = 30)]
        public virtual string BccEmail { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the Forms inbox should be bcc'd</summary>
        [Setting("Send BCC To Forms Inbox?", Description = "Select to send a BCC to the \"Forms\" inbox.", View = "Checkbox", DisplayOrder = 40)]
        public virtual string BccFormsInbox { get; set; } = string.Empty;

        /// <summary>Gets or sets the from email address.</summary>
        [Setting("From Email", Description = "The \"author's\" email address that defines who the email is from. If not provided the default email address from your system configuration will be used.", View = "TextField", SupportsPlaceholders = true, DisplayOrder = 50)]
        public virtual string FromEmail { get; set; } = string.Empty;

        /// <summary>Gets or sets the sender email address.</summary>
        [Setting("Sender Email", Description = "The sender may differ from the \"from\" address if the message was sent on behalf of someone else. Leave blank to use \"From Email\".", View = "TextField", SupportsPlaceholders = true, DisplayOrder = 60)]
        public virtual string SenderEmail { get; set; } = string.Empty;

        /// <summary>Gets or sets the email subject.</summary>
        [Setting("Reply To Email", Description = "Enter the reply-to email address(es). Semi-colon or comma delimited if more than one.", View = "TextField", SupportsPlaceholders = true, DisplayOrder = 70)]
        public virtual string ReplyToEmail { get; set; } = string.Empty;

        /// <summary>Gets or sets the email subject.</summary>
        [Setting("Subject", Description = "Enter the email subject. Required.", View = "TextField", SupportsPlaceholders = true, IsMandatory = true, DisplayOrder = 80)]
        public virtual string Subject { get; set; } = string.Empty;

        /// <summary>Gets or sets the email used on the certificate.</summary>
        [Setting("Sign Email?", Description = "Select to digitally the sign email. It will automatically be encrypted.", View = "Checkbox", DisplayOrder = 100)]
        public virtual string SignEmail { get; set; } = string.Empty;

        public override List<Exception> ValidateSettings() {
            List<Exception> list = new List<Exception>();
            if (string.IsNullOrWhiteSpace(Email)) {
                list.Add(new ArgumentNullException("Email", "'Recipient Email' setting is empty."));
            }
            if (string.IsNullOrWhiteSpace(Subject)) {
                list.Add(new ArgumentNullException("Subject", "'Subject' setting is empty.'"));
            }
            return list;
        }

        /// <summary>
        /// Summary: Creates an MimePart attachment for an email from a file held in a file system.
        /// </summary>
        /// <param name="fileSystem">The file system.</param>
        /// <param name="filePath">The path to the file within the media system.</param>
        /// <param name="attachment">The created email attachment.</param>
        /// <returns>boolean</returns>
        /// <remarks>
        /// Remarks: You must call CleanUpAttachments() to dispose of the stream(s) and delete the physical files.
        /// </remarks>
        protected bool TryCreateAttachment(IFileSystem fileSystem, string filePath, [NotNullWhen(true)] out MimePart? attachment) {
            string text = _hostingEnvironment.ToAbsolute(_globalSettings.UmbracoMediaPath).TrimEnd(Umbraco.Cms.Core.Constants.CharArrays.ForwardSlash);
            if (filePath.StartsWith(text)) {
                string text2 = filePath;
                int length = text.Length;
                filePath = text2.Substring(length, text2.Length - length);
            }

            absolutePaths.Add(fileSystem.GetFullPath(filePath));

            if (fileSystem.FileExists(filePath)) {
                //filePaths.Add(filePath);
                Stream stream = fileSystem.OpenFile(filePath);
                string fileName = Path.GetFileName(filePath);

                if (stream != null) {
                    attachment = new MimePart(MimeTypes.GetMimeType(fileName)) {
                        Content = new MimeContent(stream, ContentEncoding.Default),
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64,
                        FileName = fileName                        
                    };
                    streams.Add(stream);
                    return true;
                }
            }

            attachment = null;
            return false;
        }

        /// <summary>
        /// Summary: Dispose of attachment file streams and physically remove file from file system
        /// </summary>
        /// <param name="formName">The form name. For error logging only.</param>
        /// <param name="formId">The unique form id. For error logging only.</param>
        /// <param name="recordId">The submission record id. For error logging only.</param>
        protected void TryCleanUpAttachments(string formName, string formId, string recordId) {
            foreach (var stream in streams) {
                stream.Dispose();
            }

            _mediaFileManager.DeleteMediaFiles(absolutePaths);

            foreach (string absolutePath in absolutePaths) {
                if (!string.IsNullOrEmpty(absolutePath)) {
                    string parentDirPath = Directory.GetParent(absolutePath)?.FullName ?? string.Empty;
                    // just want to make sure in case (for some crazy reason) we're pointing at a directory higher up.
                    if (!string.IsNullOrEmpty(parentDirPath) && parentDirPath.Contains("\\wwwroot\\media\\forms\\upload")) {
                        try {
                            Directory.Delete(parentDirPath);
                        }
                        catch (DirectoryNotFoundException e) {
                            // ignore exceptions. directory wasn't deleted
                        }
                    }

                    if (Directory.Exists(parentDirPath)) {
                        _logger.LogError($"Failed to delete directory {parentDirPath} when cleaning up attachments for form {formName} | form id {formId} | record id {recordId}.");
                    }
                }
            }
        }
    }
}
