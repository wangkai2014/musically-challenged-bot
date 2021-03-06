﻿using System;
using Dapper.Contrib.Extensions;
using NodaTime;

namespace musicallychallenged.Domain
{
    [Table("Vote")]
    public class Vote
    {        
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public int ContestEntryId { get; set; }

        public Instant Timestamp { get; set; }
        
        public int Value { get; set; }
    }
}