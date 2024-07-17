using System.Text;

namespace MineguideEPOCParser.Core
{
    /// <summary>
    /// Transforms a JSON grouping file into a CSV mapper dictionary.
    /// CSV format: Include,Name,NewName
    /// (Include is always True)
    /// </summary>
    public class GroupingToMapperTransformer
    {
        public static async Task Transform(string inputGroupingFile, string outputMapperFile, bool onlyTopCategories, CancellationToken cancellationToken = default)
        {
            var groups = await MedicationMapperGroupingParser.LoadGroups(inputGroupingFile, cancellationToken);

            if (groups == null)
            {
                throw new InvalidOperationException("The grouping file could not be loaded.");
            }

            IEnumerable<(string Medication, string Category)>? medicationsByCategory = null;

            if (onlyTopCategories)
            {
                medicationsByCategory = GetMedicationsByTopCategories(groups);
            }
            else
            {
                medicationsByCategory = groups.SelectMany(p => p.Value.Select(medication => (medication, p.Key)));
            }

            // Order alphabetically by medication name (for convenience)
            medicationsByCategory = medicationsByCategory.OrderBy(p => p.Medication);

            var csv = new StringBuilder();
            csv.AppendLine("Include;Name;NewName");

            foreach (var (medication, category) in medicationsByCategory)
            {
                csv.AppendLine($"True;{medication};{category}");
            }

            await File.WriteAllTextAsync(outputMapperFile, csv.ToString(), cancellationToken);
        }

        private static IEnumerable<(string Medication, string Category)> GetMedicationsByTopCategories(Dictionary<string, string[]> groups)
        {
            // Key: Category name, Value: Category object
            Dictionary<string, EventCategory> topCategories = [];

            Dictionary<EventCategory, string[]> subCategories = [];

            foreach (var group in groups)
            {
                var categoryFullName = group.Key;
                var category = EventCategory.Parse(categoryFullName);

                if (category.Category is null)
                {
                    throw new InvalidOperationException($"Invalid category: {category.FullName}");
                }

                if (category.SubCategory is null)
                {
                    topCategories.Add(category.Category, category);

                    foreach (var medication in group.Value)
                    {
                        yield return (medication, categoryFullName);
                    }
                }
                else
                {
                    subCategories.Add(category, group.Value);
                }
            }

            foreach (var subCategory in subCategories)
            {
                foreach (var medication in subCategory.Value)
                {
                    var topCategoryFullName = topCategories[subCategory.Key.Category!].FullName;
                    yield return (medication, topCategoryFullName);
                }
            }
        }


        /// <summary>
		/// An event category, with a category, subcategory, and name.
		/// 
		/// <para>
		/// It can be parsed from a string, expecting the format: "Category.SubCategory. Name".
		/// Example: "A22. Broncodilatadores - Anticolinergicos - Lama - Accion Larga"
		/// </para>
		/// </summary>
		public sealed class EventCategory : IComparable<EventCategory>, IEquatable<EventCategory>
        {
            public string? Category { get; set; }
            public string? SubCategory { get; set; }
            public string Name { get; set; } = string.Empty;

            public string CategoryAndSubCategory => $"{Category}{SubCategory}";
            public string FullName => Category is null && SubCategory is null ? Name : $"{CategoryAndSubCategory}. {Name}";

            public override string ToString() => FullName;

            // Compare by Category and SubCategory, or the FullName if they are null (needed for sorting)
            public int CompareTo(EventCategory? other)
            {
                if (Category is null && SubCategory is null)
                {
                    return FullName.CompareTo(other?.FullName);
                }

                return CategoryAndSubCategory.CompareTo(other?.CategoryAndSubCategory);
            }

            // For equality, compare by Category and SubCategory, or the FullName if they are null
            public bool Equals(EventCategory? other)
            {
                if (Category is null && SubCategory is null)
                {
                    return FullName == other?.FullName;
                }

                return Category == other?.Category && SubCategory == other?.SubCategory;
            }

            public override bool Equals(object? obj) => Equals(obj as EventCategory);

            // For hashing, combine the hash codes of Category and SubCategory,
            // or the hash code of the FullName if they are null
            public override int GetHashCode()
            {
                if (Category is null && SubCategory is null)
                {
                    return FullName.GetHashCode();
                }

                return HashCode.Combine(Category, SubCategory);
            }

            /// <summary>
            /// Parse an event category from a string, expecting the format: "Category.SubCategory. Name".
            /// <para>
            /// Example: "A22. Broncodilatadores - Anticolinergicos - Lama - Accion Larga"
            /// </para>
            /// </summary>
            public static EventCategory Parse(string category)
            {
                int index = 0;
                StringBuilder categoryBuilder = new(category.Length);
                StringBuilder subCategoryBuilder = new(category.Length);
                StringBuilder nameBuilder = new(category.Length);

                // Example of category: "A22. Broncodilatadores - Anticolinergicos - Lama - Accion Larga"
                // Category: "A"
                // SubCategory: "22"
                // Name: "Broncodilatadores - Anticolinergicos - Lama - Accion Larga"

                // Get the category by reading the first letters until a non-letter character is found
                while (index < category.Length && char.IsLetter(category[index]))
                {
                    categoryBuilder.Append(category[index]);
                    index++;
                }

                // Now, get the subcategory by reading the next characters (which should be numbers) until the dot is found
                while (index < category.Length && category[index] != '.')
                {
                    subCategoryBuilder.Append(category[index]);
                    index++;
                }

                // Skip the dot
                index++;

                // Skip any spaces after the dot
                while (index < category.Length && char.IsWhiteSpace(category[index]))
                {
                    index++;
                }

                // Finally, get the name by reading the rest of the string
                while (index < category.Length)
                {
                    nameBuilder.Append(category[index]);
                    index++;
                }

                return new EventCategory
                {
                    Category = categoryBuilder.ToString(),
                    SubCategory = subCategoryBuilder.ToString(),
                    Name = nameBuilder.ToString()
                };
            }

            public static bool operator ==(EventCategory left, EventCategory right)
            {
                if (left is null)
                {
                    return right is null;
                }

                return left.Equals(right);
            }

            public static bool operator !=(EventCategory left, EventCategory right)
            {
                return !(left == right);
            }

            public static bool operator <(EventCategory left, EventCategory right)
            {
                return left is null ? right is not null : left.CompareTo(right) < 0;
            }

            public static bool operator <=(EventCategory left, EventCategory right)
            {
                return left is null || left.CompareTo(right) <= 0;
            }

            public static bool operator >(EventCategory left, EventCategory right)
            {
                return left is not null && left.CompareTo(right) > 0;
            }

            public static bool operator >=(EventCategory left, EventCategory right)
            {
                return left is null ? right is null : left.CompareTo(right) >= 0;
            }
        }
    }
}
