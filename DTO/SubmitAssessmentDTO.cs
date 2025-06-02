using System;
using System.Collections.Generic;

namespace EdySyncProject.DTO
{
    public class SubmitAssessmentDTO
    {
        public Guid AssessmentId { get; set; }
        public List<QuestionAnswerDTO> Answers { get; set; } = new List<QuestionAnswerDTO>();
    }

    public class QuestionAnswerDTO
    {
        public Guid QuestionId { get; set; }
        public string SelectedOption { get; set; } = string.Empty;
    }
}
