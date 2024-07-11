using System.Diagnostics.Eventing.Reader;
using System.Runtime.Serialization;
using Umbraco.Cms.Core.Models.ContentEditing;

namespace S3.Forms.Models;

public class TrackingField : IEquatable<TrackingField> {
    // summary: Gets or sets the value to use for the tracking field
    //[DataMember(Name = "value")]
    public string Value { get; set; } = string.Empty;

    //public override bool Equals(object? obj) {
    //    return base.Equals(obj);
    //}

    public override bool Equals(object? obj) {
        if (obj == null) return false;
        TrackingField tf = obj as TrackingField;
        if (tf == null) {
            return false;
        }
        else {
            return Equals(tf);
        }
    }

    public bool Equals(TrackingField? tf) {
        if (tf == null) return false;
        return (Value.Equals(tf.Value));
    }
}