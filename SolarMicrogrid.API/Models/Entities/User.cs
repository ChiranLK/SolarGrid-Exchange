using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SolarMicrogrid.API.Models.Entities
{
    public enum UserRole
    {
        Backoffice,
        GridOperator,
        Prosumer
    }

    public enum UserStatus
    {
        PendingActivation,
        Active,
        Deactivated
    }

    public class User
    {
       
        [BsonId]
        public string Nic { get; set; } = string.Empty;

        [BsonElement("full_name")]
        public string FullName { get; set; } = string.Empty;

    
        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("phone")]
        public string Phone { get; set; } = string.Empty;

        
        [BsonElement("address")]
        [BsonIgnoreIfNull]
        public string? Address { get; set; }

        [BsonElement("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [BsonElement("role")]
        [BsonRepresentation(BsonType.String)]
        public UserRole Role { get; set; }

        
        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public UserStatus Status { get; set; } = UserStatus.PendingActivation;

        
        [BsonElement("deactivation_requested")]
        public bool DeactivationRequested { get; set; } = false;

        
        [BsonElement("assigned_station_id")]
        [BsonIgnoreIfNull]
        public string? AssignedStationId { get; set; }

        
        [BsonElement("created_at")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; }

       
        [BsonElement("updated_at")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime UpdatedAt { get; set; }

    
        [BsonElement("last_login_at")]
        [BsonIgnoreIfNull]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime? LastLoginAt { get; set; }
    }

    
}