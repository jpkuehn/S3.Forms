using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using Umbraco.Forms.Core;

using IScope = Umbraco.Cms.Infrastructure.Scoping.IScope;

namespace S3.Forms.Workflows {
    public interface IWorkflowHelper {

        void InsertWorkflowAuditTrailRecord(Record record, Form form, FormState state, Workflow workflow, Guid workflowTypeId);

    }
}
