using System;

namespace EdySyncProject.DTO
{
    public class EnrolledCourseDTO
    {
        public Guid CourseId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? MediaUrl { get; set; }
        public Guid InstructorId { get; set; }
        public string? InstructorName { get; set; }
        public DateTime EnrollmentDate { get; set; }
        
        // You can add more properties as needed, such as:
        // - Progress percentage
        // - Last accessed date
        // - Next assignment due date
        // - Course completion status
    }
}
