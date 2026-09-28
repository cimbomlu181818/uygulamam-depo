using System;
using System.Collections.Generic;
using System.Globalization;

namespace DEPO_DURUMU.Data
{
    /// <summary>
    /// Yazım benzerliğini ölçer ("Şunu mu demek istediniz?" uyarısı için kullanılır).
    /// </summary>
    public static class SimilarityHelper
    {
        /// <summary>
        /// Aday listesinde, verilen yazıya en çok benzeyen (ama birebir aynı olmayan) adı bulur.
        /// Yeterince yakın bir eşleşme yoksa null verir.
        /// </summary>
        public static string FindClosestMatch(string text, IEnumerable<string> candidates)
        {
            var turkish = new CultureInfo("tr-TR");
            var normalizedText = text.Trim().ToLower(turkish);

            string bestMatch = null;
            var bestDistance = int.MaxValue;

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = candidate.Trim().ToLower(turkish);

                if (normalizedCandidate == normalizedText)
                {
                    continue; // birebir aynı adlar zaten ayrıca engelleniyor
                }

                var distance = LevenshteinDistance(normalizedText, normalizedCandidate);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = candidate;
                }
            }

            return bestDistance <= MaxAllowedDistance(normalizedText.Length) ? bestMatch : null;
        }

        /// <summary>
        /// Kısa adlarda bir harf farkı, uzun adlarda birkaç harf farkı "benzer" sayılır.
        /// </summary>
        private static int MaxAllowedDistance(int length)
        {
            if (length <= 4)
            {
                return 1;
            }

            if (length <= 8)
            {
                return 2;
            }

            return 3;
        }

        /// <summary>
        /// İki yazı arasındaki Levenshtein mesafesi: birini diğerine çevirmek için gereken
        /// en az ekleme, silme ve değiştirme sayısı.
        /// </summary>
        private static int LevenshteinDistance(string a, string b)
        {
            var distance = new int[a.Length + 1, b.Length + 1];

            for (var i = 0; i <= a.Length; i++)
            {
                distance[i, 0] = i;
            }

            for (var j = 0; j <= b.Length; j++)
            {
                distance[0, j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[a.Length, b.Length];
        }
    }
}