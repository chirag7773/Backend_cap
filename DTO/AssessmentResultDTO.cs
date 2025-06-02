using System;

namespace EdySyncProject.DTO
{
    public class AssessmentResultDTO
    {
        public Guid ResultId { get; set; }
        public Guid AssessmentId { get; set; }
        public required string AssessmentTitle { get; set; }
        public required string CourseName { get; set; }
        public int Score { get; set; }
        public int MaxScore { get; set; }
        public DateTime AttemptDate { get; set; }
        public bool IsPassed => Score >= (MaxScore * 0.6); // 60% is passing
    }
}
