using Microsoft.AspNetCore.Mvc;
using Npgsql;
using turfmanagement.Connection;
using System.Globalization;
using System.Linq;

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

                // STEP 1: Generate time slots to be booked and verify availability
                List<string> timeSlots = new List<string>();
                try
                {
                    // Parse times to generate the slot list
                    DateTime referenceDate = new DateTime(2000, 1, 1); // Reference date for time parsing
                    DateTime from, to;
                    
                    // Parse from time
                    from = DateTime.ParseExact(
                        dto.SlotTimeFrom,
                        new[] { "h tt", "hh tt" },
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None);

                    // Parse to time with special handling for 12 AM
                    if (dto.SlotTimeTo == "12 AM")
                    {
                        to = referenceDate.AddDays(1).Date; // Midnight of next day
                    }
                    else
                    {
                        to = DateTime.ParseExact(
                            dto.SlotTimeTo,
                            new[] { "h tt", "hh tt" },
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None);
                    }

                    // Only keep the time parts with the reference date
                    from = referenceDate.Date.Add(from.TimeOfDay);
                    if (dto.SlotTimeTo != "12 AM")
                    {
                        to = referenceDate.Date.Add(to.TimeOfDay);
                    }

                    // Adjust if end time is earlier in the day than start time
                    if (to <= from && dto.SlotTimeTo != "12 AM")
                    {
                        to = to.AddDays(1);
                    }

                    // Generate the time slots between from and to
                    for (DateTime time = from; time < to; time = time.AddHours(1))
                    {
                        string timeStr = time.ToString("h tt");
                        timeSlots.Add(timeStr);
                    }

                    Console.WriteLine($"📊 Generated time slots to verify: {string.Join(", ", timeSlots)}");
                }
                catch (FormatException ex)
                {
                    tran.Rollback();
                    Console.WriteLine($"❌ Time format error: {ex.Message}");
                    return BadRequest(new { message = $"Invalid time format: {ex.Message}" });
                }

                // STEP 2: Check availability of all slots with row-level locking
                foreach (var timeSlot in timeSlots)
                {
                    string checkAvailability = @"
                        SELECT Status 
                        FROM Slots 
                        WHERE SlotDate = @date AND SlotTime = @time
                        FOR UPDATE;
                    ";

                    using var cmdCheck = new NpgsqlCommand(checkAvailability, conn);
                    cmdCheck.Parameters.AddWithValue("@date", bookingDate.Date);
                    cmdCheck.Parameters.AddWithValue("@time", timeSlot);
                    cmdCheck.Transaction = tran;

                    var existingStatus = cmdCheck.ExecuteScalar()?.ToString();

                    if (existingStatus == "Unavailable")
                    {
                        tran.Rollback();
                        Console.WriteLine($"❌ Slot {timeSlot} is already booked");
                        return Conflict(new { 
                            message = $"Slot {timeSlot} is already booked by another user. Please select different time slots.",
                            conflictSlot = timeSlot,
                            status = "SlotUnavailable"
                        });
                    }

                    if (existingStatus == "Maintenance")
                    {
                        tran.Rollback();
                        Console.WriteLine($"❌ Slot {timeSlot} is under maintenance");
                        return BadRequest(new { 
                            message = $"Slot {timeSlot} is currently under maintenance. Please select different time slots.",
                            conflictSlot = timeSlot,
                            status = "SlotMaintenance"
                        });
                    }
                }

                Console.WriteLine($"✅ All slots are available for booking");

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

                // STEP 3: Mark all verified slots as unavailable
                foreach (var timeSlot in timeSlots)
                {
                    string insertSlot = @"
                        INSERT INTO Slots (SlotDate, SlotTime, Status)
                        VALUES (@date, @time, 'Unavailable')
                        ON CONFLICT (SlotDate, SlotTime) DO UPDATE
                        SET Status = 'Unavailable';
                    ";

                    using var cmdSlot = new NpgsqlCommand(insertSlot, conn);
                    cmdSlot.Parameters.AddWithValue("@date", bookingDate.Date);
                    cmdSlot.Parameters.AddWithValue("@time", timeSlot);
                    cmdSlot.Transaction = tran;
                    cmdSlot.ExecuteNonQuery();
                }

                Console.WriteLine($"✅ Marked slots as unavailable: {string.Join(", ", timeSlots)}");

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



        [HttpPost("verify")]
        public IActionResult VerifySlotAvailability([FromBody] SlotCheckDto dto)
        {
            Console.WriteLine($"🔍 Verifying slots on {dto.SlotDate} for times: {string.Join(", ", dto.SlotTimes)}");

            if (!DateTime.TryParse(dto.SlotDate, out DateTime slotDate))
                return BadRequest(new { message = "Invalid date format" });

            using var conn = _db.GetConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            try
            {
                var unavailableSlots = new List<string>();

                foreach (var time in dto.SlotTimes)
                {
                    // Use row-level locking to prevent concurrent modifications
                    string query = @"
                        SELECT Status 
                        FROM Slots 
                        WHERE SlotDate = @date AND SlotTime = @time
                        FOR UPDATE NOWAIT;
                    ";

                    using var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@date", slotDate.Date);
                    cmd.Parameters.AddWithValue("@time", time);
                    cmd.Transaction = tran;

                    try
                    {
                        var status = cmd.ExecuteScalar()?.ToString();

                        if (status == "Unavailable")
                        {
                            unavailableSlots.Add(time);
                            Console.WriteLine($"❌ Slot {time} is already booked.");
                        }
                        else if (status == "Maintenance")
                        {
                            unavailableSlots.Add(time);
                            Console.WriteLine($"❌ Slot {time} is under maintenance.");
                        }
                    }
                    catch (NpgsqlException ex) when (ex.SqlState == "55P03") // Lock not available
                    {
                        // Another transaction is currently modifying this slot
                        unavailableSlots.Add(time);
                        Console.WriteLine($"❌ Slot {time} is currently being processed by another booking.");
                    }
                }

                tran.Rollback(); // We're only checking, not modifying

                if (unavailableSlots.Any())
                {
                    Console.WriteLine($"❌ Some slots are not available: {string.Join(", ", unavailableSlots)}");
                    return Ok(new { 
                        status = "Unavailable", 
                        message = $"The following slots are not available: {string.Join(", ", unavailableSlots)}",
                        unavailableSlots = unavailableSlots
                    });
                }

                Console.WriteLine("✅ All slots are available.");
                return Ok(new { 
                    status = "Available", 
                    message = "All selected slots are available.",
                    unavailableSlots = new List<string>()
                });
            }
            catch (Exception ex)
            {
                tran.Rollback();
                Console.WriteLine($"❌ Error verifying slots: {ex.Message}");
                return StatusCode(500, new { 
                    message = "Error verifying slot availability", 
                    error = ex.Message 
                });
            }
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


    public class SlotCheckDto
    {
        public string SlotDate { get; set; }  // e.g. "2025-07-01"
        public List<string> SlotTimes { get; set; }  // e.g. ["3 PM", "4 PM"]
    }
}