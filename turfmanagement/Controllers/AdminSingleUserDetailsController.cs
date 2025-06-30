using Microsoft.AspNetCore.Mvc;

using Npgsql;

using turfmanagement.Connection;

using System;

using System.Collections.Generic;
 
namespace turfmanagement.Controllers

{

    [ApiController]

    [Route("api/[controller]")]

    public class AdminSingleUserDetailsController : ControllerBase

    {

        private readonly DatabaseConnection _db;
 
        public AdminSingleUserDetailsController(DatabaseConnection db)

        {

            _db = db;

        }
 
        [HttpGet("user-details/{phoneNumber}")]

        public IActionResult GetUserFullDetails(string phoneNumber)

        {

            using var conn = _db.GetConnection();

            conn.Open();

            // 1. Get user info + total bookings
            string query = @"

                SELECT 

                    u.UserId,

                    u.Name,

                    u.PhoneNumber,

                    u.LastBookingDate,
                    COUNT(b.BookingId) AS TotalBookings
                FROM Users u

                LEFT JOIN Bookings b ON u.UserId = b.UserId

                WHERE u.PhoneNumber = @phone

                GROUP BY u.UserId;

            ";
 
            using var cmd = new NpgsqlCommand(query, conn);

            cmd.Parameters.AddWithValue("@phone", phoneNumber);
 
            var reader = cmd.ExecuteReader();

            if (!reader.Read()) return NotFound(new { message = "User not found" });
 
            int userId = Convert.ToInt32(reader["UserId"]);

            DateTime? lastBookingDate = reader["LastBookingDate"] is DBNull

                ? (DateTime?)null

                : Convert.ToDateTime(reader["LastBookingDate"]);
 
            var user = new FullUserDetailDto

            {

                Name = reader["Name"].ToString(),

                PhoneNumber = reader["PhoneNumber"].ToString(),

                TotalBookings = Convert.ToInt32(reader["TotalBookings"]),

                LastBooking = lastBookingDate == null ? "N/A" : lastBookingDate.Value.ToString("dd/MM/yyyy"),
                UpcomingBookings = new List<BookingSummaryDto>(),

                PastBookings = new List<BookingSummaryDto>()

            };

            reader.Close();

            // 2. Get all bookings to separate past/upcoming AND calculate total time
            string bookingsQuery = @"
                SELECT BookingDate, SlotTimeFrom, SlotTimeTo
                FROM Bookings
                WHERE UserId = @uid
                ORDER BY BookingDate, SlotTimeFrom;
            ";

            using var bookingsCmd = new NpgsqlCommand(bookingsQuery, conn);
            bookingsCmd.Parameters.AddWithValue("@uid", userId);

            using var bookingsReader = bookingsCmd.ExecuteReader();
            DateTime now = DateTime.Now;
            int totalMinutes = 0;

            while (bookingsReader.Read())
            {
                var bookingDate = Convert.ToDateTime(bookingsReader["BookingDate"]);
                var from = bookingsReader["SlotTimeFrom"].ToString();
                var to = bookingsReader["SlotTimeTo"].ToString();

                var dto = new BookingSummaryDto
                {
                    Date = bookingDate.ToString("dd/MM/yyyy"),
                    TimeFrom = from,
                    TimeTo = to
                };

                // Parse start and end times
                bool validStart = DateTime.TryParse($"{bookingDate:yyyy-MM-dd} {from}", out DateTime startTime);
                bool validEnd = DateTime.TryParse($"{bookingDate:yyyy-MM-dd} {to}", out DateTime endTime);

                if (validStart && validEnd)
                {
                    // Handle overnight slots (e.g. 10 PM – 2 AM next day)
                    if (endTime <= startTime)
                        endTime = endTime.AddDays(1);

                    totalMinutes += (int)(endTime - startTime).TotalMinutes;

                    if (endTime <= now)
                        user.PastBookings.Add(dto);
                    else if (startTime > now)
                        user.UpcomingBookings.Add(dto);
                    // optional: handle ongoing booking if needed
                }
            }
            bookingsReader.Close();

            user.TotalHours = $"{(totalMinutes / 60)}Hrs";

            return Ok(user);

        }

    }
 
    public class FullUserDetailDto

    {

        public string Name { get; set; }

        public string PhoneNumber { get; set; }

        public int TotalBookings { get; set; }

        public string LastBooking { get; set; }

        public string TotalHours { get; set; }

        public List<BookingSummaryDto> UpcomingBookings { get; set; }

        public List<BookingSummaryDto> PastBookings { get; set; }

    }
 
    public class BookingSummaryDto

    {

        public string Date { get; set; }

        public string TimeFrom { get; set; }

        public string TimeTo { get; set; }

    }

}

 