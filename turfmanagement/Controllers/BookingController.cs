using Microsoft.AspNetCore.Mvc;
using Npgsql;
using turfmanagement.Connection;
using System.Globalization;

namespace turfmanagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BookingController : ControllerBase
    {
        private readonly DatabaseConnection _db;

        public BookingController(DatabaseConnection db)
        {
            _db = db;
        }

        [HttpPost("book")]
        public IActionResult BookSlot([FromBody] BookSlotDto dto)
        {
            Console.WriteLine($"📥 Received booking request:");
            Console.WriteLine($"  UserId: {dto.UserId}");
            Console.WriteLine($"  BookingDate: {dto.BookingDate}");
            Console.WriteLine($"  SlotTimeFrom: {dto.SlotTimeFrom}");
            Console.WriteLine($"  SlotTimeTo: {dto.SlotTimeTo}");
            Console.WriteLine($"  Amount: {dto.Amount}");

            using var conn = _db.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            try
            {
                // Parse the booking date from string
                if (!DateTime.TryParse(dto.BookingDate, out DateTime bookingDate))
                {
                    return BadRequest(new { message = "Invalid date format" });
                }

                // Ensure the Slots table has the necessary constraints
                // This will silently proceed if the constraint already exists
                try
                {
                    string createConstraint = @"
                        DO $$
                        BEGIN
                            IF NOT EXISTS (
                                SELECT 1
                                FROM pg_constraint
                                WHERE conname = 'slot_date_time_unique'
                            ) THEN
                                ALTER TABLE Slots ADD CONSTRAINT slot_date_time_unique UNIQUE (SlotDate, SlotTime);
                            END IF;
                        END $$;
                    ";
                    using var cmdConstraint = new NpgsqlCommand(createConstraint, conn);
                    cmdConstraint.Transaction = tran;
                    cmdConstraint.ExecuteNonQuery();
                }
                catch
                {
                    // Ignore errors with constraint creation - it's just a safety measure
                }

                // Insert booking
                string insertBooking = @"
                    INSERT INTO Bookings (UserId, BookingDate, SlotTimeFrom, SlotTimeTo, Amount)
                    VALUES (@userId, @date, @from, @to, @amount)
                    RETURNING BookingId;
                ";

                using var cmdBooking = new NpgsqlCommand(insertBooking, conn);
                cmdBooking.Parameters.AddWithValue("@userId", dto.UserId);
                cmdBooking.Parameters.AddWithValue("@date", bookingDate.Date);
                cmdBooking.Parameters.AddWithValue("@from", dto.SlotTimeFrom);
                cmdBooking.Parameters.AddWithValue("@to", dto.SlotTimeTo);
                cmdBooking.Parameters.AddWithValue("@amount", dto.Amount);
                cmdBooking.Transaction = tran;

                int bookingId = (int)cmdBooking.ExecuteScalar();

                // Insert each slot into Slots table
                try
                {
                    Console.WriteLine($"⏰ Parsing time slots from {dto.SlotTimeFrom} to {dto.SlotTimeTo}");
                    
                    // Parse times - frontend sends "2 PM" format or "12 AM" format
                    // Create a reference date for parsing
                    DateTime referenceDate = DateTime.Today; // Use today as base
                    
                    // Parse from time
                    DateTime from = DateTime.ParseExact(
                        dto.SlotTimeFrom, 
                        new[] { "h tt", "hh tt" }, // Support both "2 PM" and "12 AM" formats
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None);
                    
                    // Special handling for "12 AM" as end time
                    DateTime to;
                    if (dto.SlotTimeTo == "12 AM")
                    {
                        // If end time is 12 AM, set it to midnight (next day)
                        to = referenceDate.AddDays(1).Date; // Midnight of next day
                    }
                    else
                    {
                        // Normal parsing for other times
                        to = DateTime.ParseExact(
                            dto.SlotTimeTo, 
                            new[] { "h tt", "hh tt" }, // Support both formats
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None);
                    }
                    
                    // Only keep the time parts with the reference date
                    from = referenceDate.Date.Add(from.TimeOfDay);
                    if (dto.SlotTimeTo != "12 AM") // Only adjust non-midnight end times
                    {
                        to = referenceDate.Date.Add(to.TimeOfDay);
                    }
                    
                    // Adjust if end time is earlier in the day than start time
                    // This means booking spans midnight
                    if (to <= from && dto.SlotTimeTo != "12 AM")
                    {
                        to = to.AddDays(1);
                    }
                    
                    Console.WriteLine($"📊 Parsed times: From={from:yyyy-MM-dd HH:mm}, To={to:yyyy-MM-dd HH:mm}");
                    
                    // Generate the time slots between from and to
                    List<string> timeSlots = new List<string>();
                    for (DateTime time = from; time < to; time = time.AddHours(1))
                    {
                        string timeStr = time.ToString("h tt"); // Format as "2 PM" to match frontend
                        timeSlots.Add(timeStr);
                        
                        string insertSlot = @"
                            INSERT INTO Slots (SlotDate, SlotTime, Status)
                            VALUES (@date, @time, 'Unavailable')
                            ON CONFLICT (SlotDate, SlotTime) DO UPDATE
                            SET Status = 'Unavailable';
                        ";

                        using var cmdSlot = new NpgsqlCommand(insertSlot, conn);
                        cmdSlot.Parameters.AddWithValue("@date", bookingDate.Date);
                        cmdSlot.Parameters.AddWithValue("@time", timeStr);
                        cmdSlot.Transaction = tran;
                        cmdSlot.ExecuteNonQuery();
                    }
                    
                    Console.WriteLine($"✅ Marked slots as unavailable: {string.Join(", ", timeSlots)}");
                }
                catch (FormatException ex)
                {
                    tran.Rollback();
                    Console.WriteLine($"❌ Time format error: {ex.Message}");
                    Console.WriteLine($"🔍 Attempted to parse: From=\"{dto.SlotTimeFrom}\", To=\"{dto.SlotTimeTo}\"");
                    return BadRequest(new { message = $"Invalid time format: {ex.Message}" });
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    Console.WriteLine($"❌ Slot insertion error: {ex.Message}");
                    Console.WriteLine($"🔍 Attempted to process slots: From=\"{dto.SlotTimeFrom}\", To=\"{dto.SlotTimeTo}\"");
                    return BadRequest(new { message = $"Error processing time slots: {ex.Message}" });
                }

                // Update user's LastBookingDate
                string updateUser = @"
                    UPDATE Users
                    SET LastBookingDate = @date
                    WHERE UserId = @userId;
                ";

                using var cmdUser = new NpgsqlCommand(updateUser, conn);
                cmdUser.Parameters.AddWithValue("@date", bookingDate.Date);
                cmdUser.Parameters.AddWithValue("@userId", dto.UserId);
                cmdUser.Transaction = tran;
                cmdUser.ExecuteNonQuery();

                tran.Commit();
                Console.WriteLine($"✅ Booking successful with ID: {bookingId}");
                return Ok(new { message = "Booking successful", bookingId });
            }
            catch (Exception ex)
            {
                tran.Rollback();
                Console.WriteLine($"❌ Booking failed: {ex.Message}");
                Console.WriteLine($"🔍 Exception details: {ex}");
                Console.WriteLine($"📄 Booking data: UserId={dto.UserId}, Date={dto.BookingDate}, From={dto.SlotTimeFrom}, To={dto.SlotTimeTo}");
                return StatusCode(500, new { message = "Booking failed", error = ex.Message });
            }
        }

        [HttpGet("user/{userId}")]
        public IActionResult GetBookingsByUser(int userId)
        {
            var bookings = new List<BookingDto>();

            using var conn = _db.GetConnection();
            conn.Open();

            string query = @"
                SELECT BookingId, UserId, BookingDate, SlotTimeFrom, SlotTimeTo, Amount
                FROM Bookings
                WHERE UserId = @userId
                ORDER BY BookingDate DESC, SlotTimeFrom;
            ";

            using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@userId", userId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bookings.Add(new BookingDto
                {
                    BookingId = (int)reader["BookingId"],
                    UserId = (int)reader["UserId"],
                    BookingDate = ((DateTime)reader["BookingDate"]).ToString("yyyy-MM-dd"),
                    SlotTimeFrom = reader["SlotTimeFrom"].ToString(),
                    SlotTimeTo = reader["SlotTimeTo"].ToString(),
                    Amount = (decimal)reader["Amount"]
                });
            }

            return Ok(bookings);
        }

        [HttpGet("all")]
        public IActionResult GetAllBookings()
        {
            var bookings = new List<BookingDto>();

            using var conn = _db.GetConnection();
            conn.Open();

            string query = @"
                SELECT BookingId, UserId, BookingDate, SlotTimeFrom, SlotTimeTo, Amount
                FROM Bookings
                ORDER BY BookingDate DESC, SlotTimeFrom;
            ";

            using var cmd = new NpgsqlCommand(query, conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bookings.Add(new BookingDto
                {
                    BookingId = (int)reader["BookingId"],
                    UserId = (int)reader["UserId"],
                    BookingDate = ((DateTime)reader["BookingDate"]).ToString("yyyy-MM-dd"),
                    SlotTimeFrom = reader["SlotTimeFrom"].ToString(),
                    SlotTimeTo = reader["SlotTimeTo"].ToString(),
                    Amount = (decimal)reader["Amount"]
                });
            }

            return Ok(bookings);
        }
    }

    public class BookSlotDto
    {
        public int UserId { get; set; }
        public string BookingDate { get; set; }  // "2025-06-24"
        public string SlotTimeFrom { get; set; }  // "02:00 PM"
        public string SlotTimeTo { get; set; }    // "05:00 PM"
        public decimal Amount { get; set; }
    }

    public class BookingDto
    {
        public int BookingId { get; set; }
        public int UserId { get; set; }
        public string BookingDate { get; set; }
        public string SlotTimeFrom { get; set; }
        public string SlotTimeTo { get; set; }
        public decimal Amount { get; set; }
    }
}
