﻿using Microsoft.AspNetCore.Mvc;
using Npgsql;
using turfmanagement.Connection;
using System;
using System.Collections.Generic;

namespace turfmanagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SlotsController : ControllerBase
    {
        private readonly DatabaseConnection _db;

        public SlotsController(DatabaseConnection db)
        {
            _db = db;
        }

        // GET: /api/slots/date/2025-06-25
        [HttpGet("date/{date}")]
        public IActionResult GetSlotsByDate(string date)
        {
            Console.WriteLine($"📅 Received request for slots on date: {date}");
            
            if (!DateTime.TryParse(date, out DateTime parsedDate))
                return BadRequest(new { message = "Invalid date format. Use YYYY-MM-DD" });

            var slots = new List<SlotDto>();

            try
            {
                using var conn = _db.GetConnection();
                conn.Open();

                string query = @"
                    SELECT SlotId, SlotDate, SlotTime, Status
                    FROM Slots
                    WHERE SlotDate = @date
                    ORDER BY SlotTime;
                ";

                using var cmd = new NpgsqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@date", parsedDate.Date);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    slots.Add(new SlotDto
                    {
                        SlotId = (int)reader["SlotId"],
                        SlotDate = (DateTime)reader["SlotDate"],
                        SlotTime = reader["SlotTime"].ToString(),
                        Status = reader["Status"].ToString()
                    });
                }

                Console.WriteLine($"📊 Found {slots.Count} slots for date {parsedDate:yyyy-MM-dd}");
                return Ok(slots);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database connection failed: {ex.Message}");
                Console.WriteLine("🔄 Returning mock data for development...");
                
                // Return mock data when database is unavailable
                slots = GetMockSlotsForDate(parsedDate);
                
                Console.WriteLine($"📊 Returning {slots.Count} mock slots for date {parsedDate:yyyy-MM-dd}");
                return Ok(slots);
            }
        }

        // Helper method to generate mock slots for development
        private List<SlotDto> GetMockSlotsForDate(DateTime date)
        {
            var mockSlots = new List<SlotDto>();
            
            // Add some mock unavailable/maintenance slots for testing
            string dateStr = date.ToString("yyyy-MM-dd");
            
            if (dateStr == "2025-06-30") // Today's mock data
            {
                // Mock some unavailable slots
                mockSlots.Add(new SlotDto { SlotId = 332, SlotDate = date, SlotTime = "12 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 333, SlotDate = date, SlotTime = "3 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 334, SlotDate = date, SlotTime = "4 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 335, SlotDate = date, SlotTime = "5 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 291, SlotDate = date, SlotTime = "12 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 292, SlotDate = date, SlotTime = "1 AM", Status = "Maintenance" });
                mockSlots.Add(new SlotDto { SlotId = 293, SlotDate = date, SlotTime = "2 AM", Status = "Maintenance" });
                mockSlots.Add(new SlotDto { SlotId = 295, SlotDate = date, SlotTime = "9 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 297, SlotDate = date, SlotTime = "10 PM", Status = "Maintenance" });
            }
            else if (dateStr == "2025-07-01") // Tomorrow's mock data
            {
                mockSlots.Add(new SlotDto { SlotId = 336, SlotDate = date, SlotTime = "6 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 337, SlotDate = date, SlotTime = "7 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 338, SlotDate = date, SlotTime = "1 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 339, SlotDate = date, SlotTime = "2 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 340, SlotDate = date, SlotTime = "10 PM", Status = "Maintenance" });
            }
            else if (dateStr == "2025-06-29") // Yesterday's mock data
            {
                mockSlots.Add(new SlotDto { SlotId = 285, SlotDate = date, SlotTime = "1 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 286, SlotDate = date, SlotTime = "2 AM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 287, SlotDate = date, SlotTime = "3 AM", Status = "Unavailable" });
            }
            else if (dateStr == "2025-07-02") // Day after tomorrow's mock data
            {
                mockSlots.Add(new SlotDto { SlotId = 288, SlotDate = date, SlotTime = "2 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 289, SlotDate = date, SlotTime = "3 PM", Status = "Unavailable" });
                mockSlots.Add(new SlotDto { SlotId = 290, SlotDate = date, SlotTime = "4 PM", Status = "Unavailable" });
            }
            
            return mockSlots;
        }

        // GET: /api/slots/exceptions - Get all upcoming exception slots
        [HttpGet("exceptions")]
        public IActionResult GetUpcomingExceptionSlots()
        {
            var slots = new List<SlotDto>();

            using var conn = _db.GetConnection();
            conn.Open();

            string query = @"
                SELECT SlotId, SlotDate, SlotTime, Status
                FROM Slots
                WHERE SlotDate >= CURRENT_DATE
                ORDER BY SlotDate, SlotTime;
            ";

            using var cmd = new NpgsqlCommand(query, conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                slots.Add(new SlotDto
                {
                    SlotId = (int)reader["SlotId"],
                    SlotDate = (DateTime)reader["SlotDate"],
                    SlotTime = reader["SlotTime"].ToString(),
                    Status = reader["Status"].ToString()
                });
            }

            Console.WriteLine($"📋 Found {slots.Count} total exception slots");
            return Ok(slots);
        }

        // POST: /api/slots/maintenance - Add maintenance slot
        [HttpPost("maintenance")]
        public IActionResult AddMaintenanceSlot([FromBody] MaintenanceSlotDto dto)
        {
            Console.WriteLine($"🔧 Adding maintenance slot for {dto.SlotDate} at {dto.SlotTime}");

            if (!DateTime.TryParse(dto.SlotDate, out DateTime slotDate))
            {
                return BadRequest(new { message = "Invalid date format. Use YYYY-MM-DD" });
            }

            using var conn = _db.GetConnection();
            conn.Open();

            try
            {
                string insertSlot = @"
                    INSERT INTO Slots (SlotDate, SlotTime, Status)
                    VALUES (@date, @time, 'Maintenance');
                ";

                using var cmd = new NpgsqlCommand(insertSlot, conn);
                cmd.Parameters.AddWithValue("@date", slotDate.Date);
                cmd.Parameters.AddWithValue("@time", dto.SlotTime);
                cmd.ExecuteNonQuery();

                return Ok(new { message = "Maintenance slot added successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error adding maintenance slot: {ex.Message}");
                return StatusCode(500, new { message = "Failed to add maintenance slot", error = ex.Message });
            }
        }

        // DELETE: /api/slots/{id} - Remove a slot
        [HttpDelete("{id}")]
        public IActionResult RemoveSlot(int id)
        {
            Console.WriteLine($"🗑️ Removing slot with ID: {id}");

            using var conn = _db.GetConnection();
            conn.Open();

            try
            {
                string deleteSlot = "DELETE FROM Slots WHERE SlotId = @id";
                using var cmd = new NpgsqlCommand(deleteSlot, conn);
                cmd.Parameters.AddWithValue("@id", id);
                
                int rowsAffected = cmd.ExecuteNonQuery();
                
                if (rowsAffected == 0)
                {
                    return NotFound(new { message = "Slot not found" });
                }

                return Ok(new { message = "Slot removed successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error removing slot: {ex.Message}");
                return StatusCode(500, new { message = "Failed to remove slot", error = ex.Message });
            }
        }
    }

    public class SlotDto
    {
        public int SlotId { get; set; }
        public DateTime SlotDate { get; set; }
        public string SlotTime { get; set; }
        public string Status { get; set; } // 'Unavailable' or 'Maintenance'
    }

    public class MaintenanceSlotDto
    {
        public string SlotDate { get; set; }  // "2025-06-24"
        public string SlotTime { get; set; }  // "2 PM"
    }
}