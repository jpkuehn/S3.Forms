using System.Data;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using Umbraco.Forms.Core.Providers;
using Umbraco.Forms.Core;

using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;
using IScope = Umbraco.Cms.Infrastructure.Scoping.IScope;
using Microsoft.Extensions.Logging;

namespace S3.Forms.Workflows {
    public class WorkflowHelper : IWorkflowHelper {
        private readonly WorkflowCollectionFactory _workflowCollectionFactory;
        private readonly IScopeProvider _scopeProvider;
        private readonly ILogger<WorkflowHelper> _logger;

        public WorkflowHelper(
            WorkflowCollectionFactory workflowCollectionFactory,
            IScopeProvider scopeProvider,
            ILogger<WorkflowHelper> logger
        ) {
            _workflowCollectionFactory = workflowCollectionFactory;
            _scopeProvider = scopeProvider;
            _logger = logger;
        }

        ///<summary>
        /// Purpose: Workflow audit trail records are only created if a form is set to save data to db.
        ///          (see Umbraco.Forms.Core.Services.WorkflowExecutionService.RecordAuditTrail)
        ///          Since we do not save data in most cases (only personally identifiable data is saved via the InsertTrackingData workflow),
        ///          we need to insert workflow audit trail records in custom workflows.
        ///</summary>
        public void InsertWorkflowAuditTrailRecord(Record record, Form form, FormState state, Workflow workflow, Guid workflowTypeId) {
            // workflow audit trail is automatically created if StoreRecordsLocally = true
            if (!form.StoreRecordsLocally) {
                using (IScope scope = this._scopeProvider.CreateScope(IsolationLevel.Unspecified, (RepositoryCacheMode)0, (IEventDispatcher)null, (IScopedNotificationPublisher)null, new bool?(), false, false)) {
                    if (workflow != null) {
                        WorkflowCollection workflowCollection = _workflowCollectionFactory.GetWorkflowCollection();
                        WorkflowType type = workflowCollection[workflowTypeId];

                        try {
                            RecordWorkflowAudit recordAudit = CreateRecordAudit(record, workflow, type, FormState.Submitted, WorkflowExecutionStatus.Completed);
                            RecordAuditRecord(scope, recordAudit);
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, $"Form workflow {(object)workflow.Name} failed on {(object)Enum.GetName(typeof(FormState), (object)state)} of Record with Unique ID {(object)record.UniqueId} from the Form named {(object)form.Name} with Unique ID {(object)form.Id}");
                        }
                    }

                    scope.Complete();
                }
            }
        }

        public RecordWorkflowAudit CreateRecordAudit(Record record, Workflow workflow, WorkflowType type, FormState formState, WorkflowExecutionStatus wfes) {
            return new RecordWorkflowAudit() {
                RecordUniqueId = record.UniqueId,
                ExecutedOn = DateTime.Now,
                ExecutionStage = new int?((int)formState),
                ExecutionStatus = (int)wfes,
                WorkflowKey = workflow.Id,
                WorkflowName = workflow.Name,
                WorkflowTypeId = workflow.WorkflowTypeId,
                WorkflowTypeName = type.Name
            };
        }

        public void RecordAuditRecord(IScope scope, RecordWorkflowAudit recordAudit) {
            // StoreRecordsLocally in Umbraco.Forms.Core.Services.WorkflowExecutionService.RecordAuditTrail() is what prevents 
            // audit trail records from getting inserted. 
            //if (form.StoreRecordsLocally) {
            //    scope.Database.Insert(recordAudit);
            //}
            scope.Database.Insert(recordAudit);
        }

    }
}
