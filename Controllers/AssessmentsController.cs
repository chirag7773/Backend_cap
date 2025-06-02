using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EdySyncProject.DTO;

namespace EdySyncProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AssessmentsController : ControllerBase
    {
        private readonly EduSyncContext _context;

        public AssessmentsController(EduSyncContext context)
        {
            _context = context;
        }

        // GET: api/Assessments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AssessmentDTO>>> GetAssessments()
        {
            var assessments = await _context.Assessments
                .Include(a => a.Questions)
                .Select(a => new AssessmentDTO
                {
                    AssessmentId = a.AssessmentId,
                    Title = a.Title,
                    MaxScore = a.MaxScore,
                    CourseId = a.CourseId,
                    Questions = a.Questions.Select(q => new QuestionDTO
                    {
                        QuestionId = q.QuestionId,
                        QuestionText = q.QuestionText,
                        OptionA = q.OptionA,
                        OptionB = q.OptionB,
                        OptionC = q.OptionC,
                        OptionD = q.OptionD
                    }).ToList()
                })
                .ToListAsync();

            return Ok(assessments);
        }

        // GET: api/Assessments/5
        [HttpGet("{id}")]
        public async Task<ActionResult<AssessmentDTO>> GetAssessment(Guid id)
        {
            var assessment = await _context.Assessments
                .Include(a => a.Questions)
                .FirstOrDefaultAsync(a => a.AssessmentId == id);

            if (assessment == null)
                return NotFound();

            var dto = new AssessmentDTO
            {
                AssessmentId = assessment.AssessmentId,
                Title = assessment.Title,
                MaxScore = assessment.MaxScore,
                CourseId = assessment.CourseId,
                Questions = assessment.Questions.Select(q => new QuestionDTO
                {
                    QuestionId = q.QuestionId,
                    QuestionText = q.QuestionText,
                    OptionA = q.OptionA,
                    OptionB = q.OptionB,
                    OptionC = q.OptionC,
                    OptionD = q.OptionD
                }).ToList()
            };

            return Ok(dto);
        }

        // GET: api/Assessments/Course/5
        [HttpGet("Course/{courseId}")]
        public async Task<ActionResult<IEnumerable<AssessmentDTO>>> GetAssessmentsByCourse(Guid courseId)
        {
            var courseExists = await _context.Courses.AnyAsync(c => c.CourseId == courseId);
            if (!courseExists)
                return NotFound("Course not found");

            var assessments = await _context.Assessments
                .Where(a => a.CourseId == courseId)
                .Select(a => new AssessmentDTO
                {
                    AssessmentId = a.AssessmentId,
                    Title = a.Title,
                    MaxScore = a.MaxScore,
                    CourseId = a.CourseId,
                    Questions = a.Questions.Select(q => new QuestionDTO
                    {
                        QuestionId = q.QuestionId,
                        QuestionText = q.QuestionText,
                        OptionA = q.OptionA,
                        OptionB = q.OptionB,
                        OptionC = q.OptionC,
                        OptionD = q.OptionD
                    }).ToList()
                })
                .ToListAsync();

            return Ok(assessments);
        }

        [HttpPost]
        [Authorize(Roles = "Instructor")]
        public async Task<ActionResult<AssessmentDTO>> PostAssessment([FromBody] CreateAssessmentDTO dto)
        {
            var courseExists = await _context.Courses.AnyAsync(c => c.CourseId == dto.CourseId);
            if (!courseExists)
                return BadRequest("CourseId is invalid or does not exist.");

            if (dto.Questions == null || !dto.Questions.Any())
                return BadRequest("At least one question is required.");

            var assessment = new Assessment
            {
                AssessmentId = Guid.NewGuid(),
                Title = dto.Title,
                MaxScore = dto.MaxScore,
                CourseId = dto.CourseId,
                Questions = dto.Questions.Select(q => new Question
                {
                    QuestionId = Guid.NewGuid(),
                    QuestionText = q.QuestionText,
                    OptionA = q.OptionA,
                    OptionB = q.OptionB,
                    OptionC = q.OptionC,
                    OptionD = q.OptionD,
                    CorrectOption = q.CorrectOption
                }).ToList()
            };

            _context.Assessments.Add(assessment);
            await _context.SaveChangesAsync();

            var result = new AssessmentDTO
            {
                AssessmentId = assessment.AssessmentId,
                Title = assessment.Title,
                MaxScore = assessment.MaxScore,
                CourseId = assessment.CourseId,
                Questions = assessment.Questions.Select(q => new QuestionDTO
                {
                    QuestionId = q.QuestionId,
                    QuestionText = q.QuestionText,
                    OptionA = q.OptionA,
                    OptionB = q.OptionB,
                    OptionC = q.OptionC,
                    OptionD = q.OptionD
                }).ToList()
            };

            return CreatedAtAction(nameof(GetAssessment), new { id = assessment.AssessmentId }, result);
        }

        [HttpPost("Submit")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SubmitAssessment([FromBody] SubmitAssessmentDTO dto)
        {
            try
            {
                // Log the incoming request for debugging
                Console.WriteLine($"[SubmitAssessment] Received request with AssessmentId: {dto?.AssessmentId}");
                Console.WriteLine($"[SubmitAssessment] Number of answers: {dto?.Answers?.Count ?? 0}");
                Console.WriteLine($"[SubmitAssessment] User claims: {string.Join(", ", User.Claims.Select(c => $"{c.Type}: {c.Value}"))}");

                if (dto == null)
                    return BadRequest(new { title = "Invalid request", detail = "Request body cannot be null" });
                    
                if (dto.Answers == null)
                    return BadRequest(new { 
                        title = "Validation error", 
                        detail = "Answers array is required"
                    });
                    
                if (!dto.Answers.Any())
                    return BadRequest(new { 
                        title = "Validation error", 
                        detail = "At least one answer is required"
                    });

                if (dto.AssessmentId == Guid.Empty)
                    return BadRequest(new { 
                        title = "Validation error", 
                        detail = "Assessment ID is required and must be a valid GUID"
                    });
                    
                // Validate each answer
                var validationErrors = new List<string>();
                for (int i = 0; i < dto.Answers.Count; i++)
                {
                    var answer = dto.Answers[i];
                    Console.WriteLine($"[SubmitAssessment] Validating answer {i}: QuestionId={answer.QuestionId}, SelectedOption={answer.SelectedOption}");
                    if (answer.QuestionId == Guid.Empty)
                    {
                        validationErrors.Add($"Answer at index {i} has an invalid QuestionId");
                    }
                    if (string.IsNullOrWhiteSpace(answer.SelectedOption))
                    {
                        validationErrors.Add($"Answer for QuestionId {answer.QuestionId} has no selected option");
                    }
                }
                
                if (validationErrors.Any())
                {
                    Console.WriteLine($"[SubmitAssessment] Validation errors: {string.Join(", ", validationErrors)}");
                    return BadRequest(new {
                        title = "Validation error",
                        detail = "One or more answers are invalid",
                        errors = validationErrors
                    });
                }

                var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
                Console.WriteLine($"[SubmitAssessment] User ID claim: {userIdClaim?.Value}");
                
                if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                    return Unauthorized(new { 
                        title = "Authentication error", 
                        detail = "Invalid user session",
                        hasUserIdClaim = userIdClaim != null,
                        userIdClaimValue = userIdClaim?.Value
                    });

                // Check if result already exists for this assessment and user
                var existingResult = await _context.Results
                    .FirstOrDefaultAsync(r => r.AssessmentId == dto.AssessmentId && r.UserId == userId);

                Console.WriteLine($"[SubmitAssessment] Existing result found: {existingResult != null}");

                var assessment = await _context.Assessments
                    .Include(a => a.Questions)
                    .FirstOrDefaultAsync(a => a.AssessmentId == dto.AssessmentId);

                if (assessment == null)
                {
                    Console.WriteLine($"[SubmitAssessment] Assessment not found with ID: {dto.AssessmentId}");
                    return NotFound(new { 
                        title = "Not found", 
                        detail = "The specified assessment was not found",
                        requestedAssessmentId = dto.AssessmentId
                    });
                }

                Console.WriteLine($"[SubmitAssessment] Found assessment with {assessment.Questions.Count} questions");
                foreach (var question in assessment.Questions)
                {
                    Console.WriteLine($"[SubmitAssessment] Question {question.QuestionId}: CorrectOption={question.CorrectOption}");
                }
                    
                // Validate that all questions in the assessment have been answered
                var unansweredQuestions = assessment.Questions
                    .Where(q => !dto.Answers.Any(a => a.QuestionId == q.QuestionId))
                    .ToList();

                Console.WriteLine($"[SubmitAssessment] Unanswered questions: {unansweredQuestions.Count}");
                    
                if (unansweredQuestions.Any())
                {
                    Console.WriteLine($"[SubmitAssessment] Unanswered questions: {string.Join(", ", unansweredQuestions.Select(q => q.QuestionId))}");
                    return BadRequest(new { 
                        title = "Validation error", 
                        detail = "All questions must be answered",
                        unansweredQuestions = unansweredQuestions.Select(q => new { 
                            q.QuestionId,
                            q.QuestionText
                        }),
                        receivedAnswers = dto.Answers.Select(a => new {
                            a.QuestionId,
                            a.SelectedOption
                        })
                    });
                }

                // Calculate score based on correct answers
                int correctCount = 0;
                foreach (var question in assessment.Questions)
                {
                    var answer = dto.Answers.FirstOrDefault(a => a.QuestionId == question.QuestionId);
                    var selectedOption = answer?.SelectedOption?.Trim().ToUpperInvariant() ?? "";
                    var correctOption = question.CorrectOption?.Trim().ToUpperInvariant() ?? "";
                    
                    Console.WriteLine($"[SubmitAssessment] Checking answer for question {question.QuestionId}:");
                    Console.WriteLine($"[SubmitAssessment] - Selected option: '{selectedOption}'");
                    Console.WriteLine($"[SubmitAssessment] - Correct option: '{correctOption}'");
                    
                    if (answer != null && selectedOption == correctOption)
                    {
                        correctCount++;
                        Console.WriteLine($"[SubmitAssessment] ✓ Correct answer for question {question.QuestionId}");
                    }
                    else
                    {
                        Console.WriteLine($"[SubmitAssessment] ✗ Incorrect answer for question {question.QuestionId}");
                    }
                }

                // Calculate final score based on max score of the assessment
                double finalScore = Math.Round((correctCount / (double)assessment.Questions.Count) * assessment.MaxScore, 2);
                int score = (int)finalScore;
                
                Console.WriteLine($"[SubmitAssessment] Final score calculation:");
                Console.WriteLine($"[SubmitAssessment] - Correct answers: {correctCount}");
                Console.WriteLine($"[SubmitAssessment] - Total questions: {assessment.Questions.Count}");
                Console.WriteLine($"[SubmitAssessment] - Max score: {assessment.MaxScore}");
                Console.WriteLine($"[SubmitAssessment] - Final score: {finalScore}");

                if (existingResult != null)
                {
                    // Update existing result
                    existingResult.Score = score;
                    existingResult.AttemptDate = DateTime.UtcNow;
                    _context.Results.Update(existingResult);
                    Console.WriteLine($"[SubmitAssessment] Updated existing result with score {score}");
                }
                else
                {
                    // Create new result
                    var newResult = new Result
                    {
                        ResultId = Guid.NewGuid(),
                        AssessmentId = dto.AssessmentId,
                        UserId = userId,
                        Score = score,
                        AttemptDate = DateTime.UtcNow
                    };
                    _context.Results.Add(newResult);
                    Console.WriteLine($"[SubmitAssessment] Created new result with score {score}");
                }

                await _context.SaveChangesAsync();
                Console.WriteLine($"[SubmitAssessment] Successfully saved result to database");

                return Ok(new 
                { 
                    score = finalScore, 
                    totalQuestions = assessment.Questions.Count, 
                    correctAnswers = correctCount,
                    maxScore = assessment.MaxScore,
                    isUpdate = existingResult != null
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SubmitAssessment] Error: {ex}");
                Console.WriteLine($"[SubmitAssessment] Stack trace: {ex.StackTrace}");
                return StatusCode(500, new 
                { 
                    title = "Internal Server Error", 
                    detail = "An error occurred while processing your request",
                    error = ex.Message
                });
            }
        }

        [HttpGet("student/results")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<IEnumerable<AssessmentResultDTO>>> GetStudentResults()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Unauthorized(new { title = "Authentication error", detail = "Invalid user session" });

            var results = await _context.Results
                .Include(r => r.Assessment)
                .ThenInclude(a => a.Course)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.AttemptDate)
                .Select(r => new AssessmentResultDTO
                {
                    ResultId = r.ResultId,
                    AssessmentId = r.AssessmentId,
                    AssessmentTitle = r.Assessment.Title,
                    CourseName = r.Assessment.Course.Title,
                    Score = r.Score,
                    MaxScore = r.Assessment.MaxScore,
                    AttemptDate = r.AttemptDate
                })
                .ToListAsync();

            return Ok(results);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Instructor")]
        public async Task<IActionResult> DeleteAssessment(Guid id)
        {
            var assessment = await _context.Assessments
                .Include(a => a.Questions)
                .FirstOrDefaultAsync(a => a.AssessmentId == id);

            if (assessment == null)
                return NotFound();

            // Remove all related questions (if not cascade)
            _context.Questions.RemoveRange(assessment.Questions);

            _context.Assessments.Remove(assessment);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
