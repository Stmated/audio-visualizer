namespace Eliason.AudioVisualizer
{
    public class Note
    {
        public string Id { get; set; }
        public Interval Interval { get; set; }
        public string Text { get; set; }
        public bool IsFocused { get; set; }

        public override int GetHashCode()
        {
            return Id != null ? Id.GetHashCode() : 0;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Note) obj);
        }

        private bool Equals(Note other)
        {
            return string.Equals(Id, other.Id);
        }
    }
}