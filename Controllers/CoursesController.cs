using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EdySyncProject.DTO;

namespace EdySyncProject.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoursesController : ControllerBase
    {
        private readonly EduSyncContext _context;

        public CoursesController(EduSyncContext context)
        {
            _context = context;
        }

        // GET: api/Courses/ByInstructor/{instructorId}
        [HttpGet("ByInstructor/{instructorId}")]
        [Authorize(Roles = "Instructor,Student")]
        public async Task<ActionResult<IEnumerable<CourseDTO>>> GetCoursesByInstructor(
            Guid instructorId, 
            [FromQuery] bool activeOnly = false)
        {
            // Validate if instructor exists
            var instructor = await _context.Users.FindAsync(instructorId);
            if (instructor == null)
            {
                return NotFound($"Instructor with ID {instructorId} not found");
            }

            var query = _context.Courses
                .Where(c => c.InstructorId == instructorId);

            // If activeOnly is true, add additional filtering logic here
            // For example, you might have an IsActive property or a status field
            // For now, we'll return all courses since we don't have an active/inactive status
            // query = query.Where(c => c.IsActive);

            var courses = await query
                .Select(course => new CourseDTO
                {
                    CourseId = course.CourseId,
                    Title = course.Title,
                    Description = course.Description,
                    MediaUrl = course.MediaUrl,
                    InstructorId = course.InstructorId,
                    InstructorName = course.Instructor.Name
                })
                .ToListAsync();

            return courses;
        }

        // GET: api/Courses/MyCourses
        [HttpGet("MyCourses")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<IEnumerable<EnrolledCourseDTO>>> GetMyCourses()
        {
            try
            {
                // Log all claims for debugging
                Console.WriteLine("=== User Claims ===");
                foreach (var claim in User.Claims)
                {
                    Console.WriteLine($"{claim.Type}: {claim.Value}");
                }

                // Get the current user's ID from the token
                var userIdClaim = User.Claims.FirstOrDefault(c => 
                    c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" ||
                    c.Type == ClaimTypes.NameIdentifier ||
                    c.Type == "sub");

                if (userIdClaim == null)
                {
                    Console.WriteLine("ERROR: Could not find user ID in claims");
                    return Unauthorized(new { message = "User ID not found in token" });
                }

                Console.WriteLine($"User ID from token: {userIdClaim.Value}");
                
                // Try to parse the user ID
                if (!Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    Console.WriteLine($"WARNING: Could not parse user ID: {userIdClaim.Value}");
                    
                    // Try to find the user by email as a fallback
                    var emailClaim = User.Claims.FirstOrDefault(c => 
                        c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress" ||
                        c.Type == ClaimTypes.Email);
                        
                    if (emailClaim == null)
                    {
                        Console.WriteLine("ERROR: Could not find email claim");
                        return Unauthorized(new { message = "Could not identify user" });
                    }
                    
                    Console.WriteLine($"Looking up user by email: {emailClaim.Value}");
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == emailClaim.Value);
                    
                    if (user == null)
                    {
                        Console.WriteLine($"ERROR: User with email {emailClaim.Value} not found");
                        return Unauthorized(new { message = "User not found" });
                    }
                    
                    userId = user.UserId;
                    Console.WriteLine($"Found user ID from email: {userId}");
                }

                Console.WriteLine($"Looking up enrollments for user ID: {userId}");
                
                // Log all enrollments for debugging
                var allEnrollments = await _context.Enrollments
                    .Include(e => e.Course)
                    .Include(e => e.User)
                    .Take(5)
                    .ToListAsync();
                    
                Console.WriteLine($"Sample of enrollments in database (first 5):");
                foreach (var e in allEnrollments)
                {
                    Console.WriteLine($"- {e.User?.Email ?? "[Unknown]"} in {e.Course?.Title ?? "[Unknown]"} (ID: {e.UserId} -> {e.CourseId})");
                }

                // Get all enrollments for the current user
                var enrolledCourses = await _context.Enrollments
                    .Where(e => e.UserId == userId)
                    .Include(e => e.Course)
                        .ThenInclude(c => c.Instructor)
                    .Select(e => new EnrolledCourseDTO
                    {
                        CourseId = e.Course.CourseId,
                        Title = e.Course.Title,
                        Description = e.Course.Description,
                        MediaUrl = e.Course.MediaUrl,
                        InstructorId = e.Course.InstructorId,
                        InstructorName = e.Course.Instructor != null ? e.Course.Instructor.Name : "[Unknown Instructor]",
                        EnrollmentDate = e.EnrollmentDate,
                    })
                    .OrderByDescending(ec => ec.EnrollmentDate)
                    .ToListAsync();

                Console.WriteLine($"Found {enrolledCourses.Count} enrolled courses for user {userId}");
                
                // If no courses found, check if the user exists in the Users table
                if (enrolledCourses.Count == 0)
                {
                    var userExists = await _context.Users.AnyAsync(u => u.UserId == userId);
                    Console.WriteLine($"User exists in database: {userExists}");
                    
                    var anyEnrollments = await _context.Enrollments.AnyAsync();
                    Console.WriteLine($"Any enrollments in database: {anyEnrollments}");
                    
                    var anyCourses = await _context.Courses.AnyAsync();
                    Console.WriteLine($"Any courses in database: {anyCourses}");
                }
                
                return Ok(enrolledCourses);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in GetMyCourses: {ex}");
                return StatusCode(500, new { 
                    message = "An error occurred while fetching enrolled courses", 
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        // GET: api/Courses
        [HttpGet]
        [Authorize(Roles = "Instructor,Student")] 
        public async Task<ActionResult<IEnumerable<CourseDTO>>> GetCourses()
        {
            var courses = await _context.Courses
                .Include(c => c.Instructor)
                .Select(course => new CourseDTO
                {
                    CourseId = course.CourseId,
                    Title = course.Title,
                    Description = course.Description,
                    MediaUrl = course.MediaUrl,
                    InstructorId = course.InstructorId,
                    InstructorName = course.Instructor != null ? course.Instructor.Name : null
                })
                .ToListAsync();

            return Ok(courses);
        }

        // GET: api/Courses/5
        [HttpGet("{id}")]
        [Authorize(Roles = "Instructor,Student")]
        public async Task<ActionResult<CourseDTO>> GetCourse(Guid id)
        {
            var course = await _context.Courses.FindAsync(id); 

            if (course == null)
            {
                return NotFound();
            }

            var courseDto = new CourseDTO
            {
                CourseId = course.CourseId,
                Title = course.Title,
                Description = course.Description,
                MediaUrl = course.MediaUrl,
                InstructorId = course.InstructorId,
                InstructorName = course.Instructor?.Name
            };

            return Ok(courseDto);
        }
        [HttpPost("{courseId}/enroll")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Enroll(Guid courseId)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c =>
                c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
            if (userIdClaim == null)
                return Unauthorized();

            var userId = Guid.Parse(userIdClaim.Value);

            // Prevent duplicate enrollment
            var alreadyEnrolled = await _context.Enrollments
                .AnyAsync(e => e.UserId == userId && e.CourseId == courseId);
            if (alreadyEnrolled)
                return BadRequest("You are already enrolled in this course.");

            var enrollment = new Enrollment
            {
                EnrollmentId = Guid.NewGuid(),
                UserId = userId,
                CourseId = courseId
            };

            _context.Enrollments.Add(enrollment);
            await _context.SaveChangesAsync();

            return Ok("Enrolled successfully!");
        }

        [HttpGet("{courseId}/enrollment-status")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> CheckEnrollmentStatus(Guid courseId)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c =>
                c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
            if (userIdClaim == null)
                return Unauthorized();

            var userId = Guid.Parse(userIdClaim.Value);

            var isEnrolled = await _context.Enrollments
                .AnyAsync(e => e.UserId == userId && e.CourseId == courseId);

            return Ok(new { enrolled = isEnrolled });
        }

        // POST: api/Courses
        [HttpPost]
        [Authorize(Roles = "Instructor")]
        public async Task<ActionResult<CourseDTO>> PostCourse([FromBody] CreateCourseDTO dto)
        {
            var instructor = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == dto.InstructorId && u.Role == "Instructor");
            if (instructor == null)
                return BadRequest("InstructorId is invalid.");

            var course = new Course
            {
                CourseId = Guid.NewGuid(),
                Title = dto.Title,
                Description = dto.Description,
                MediaUrl = dto.MediaUrl,
                InstructorId = dto.InstructorId
            };

            _context.Courses.Add(course);
            await _context.SaveChangesAsync();

            var result = new CourseDTO
            {
                CourseId = course.CourseId,
                Title = course.Title,
                Description = course.Description,
                MediaUrl = course.MediaUrl,
                InstructorId = course.InstructorId
            };

            return CreatedAtAction(nameof(GetCourse), new { id = course.CourseId }, result);
        }

        // PUT: api/Courses/5
        [HttpPut("{id}")]
        [Authorize(Roles = "Instructor")]
        public async Task<IActionResult> PutCourse(Guid id, [FromBody] CreateCourseDTO dto)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null)
                return NotFound();

            if (course.InstructorId != dto.InstructorId)
                return Forbid();

            course.Title = dto.Title;
            course.Description = dto.Description;
            course.MediaUrl = dto.MediaUrl;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/Courses/5
        [HttpDelete("{id}")]
        [Authorize(Roles = "Instructor")]
        public async Task<IActionResult> DeleteCourse(Guid id)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null)
                return NotFound();

            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CourseExists(Guid id)
        {
            return _context.Courses.Any(e => e.CourseId == id);
        }
    }
}
