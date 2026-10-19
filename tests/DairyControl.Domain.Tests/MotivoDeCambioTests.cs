using DairyControl.Domain.Exceptions;
using DairyControl.Domain.ValueObjects;

namespace DairyControl.Domain.Tests
{
    public class MotivoDeCambioTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Create_BlankReason_ThrowsReglaDeNegocioExceptionOnMotivo(string? motivo)
        {
            // Arrange & Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => MotivoDeCambio.Create(motivo));

            // Assert
            Assert.Equal("motivo", ex.Campo);
        }

        [Theory]
        [InlineData("abcd")]
        [InlineData("  abcd  ")]
        public void Create_FewerThanFiveCharactersAfterTrimming_Throws(string motivo)
        {
            // Arrange & Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => MotivoDeCambio.Create(motivo));

            // Assert
            Assert.Equal("motivo", ex.Campo);
            Assert.Contains("al menos 5", ex.Message);
        }

        [Fact]
        public void Create_ExactlyFiveCharacters_IsValidAndTrimmed()
        {
            // Arrange & Act
            var motivo = MotivoDeCambio.Create("  litro  ");

            // Assert
            Assert.Equal("litro", motivo.Texto);
        }

        [Fact]
        public void Create_OverTheMaximum_Throws()
        {
            // Arrange
            var largo = new string('x', MotivoDeCambio.MaxLength + 1);

            // Act
            var ex = Assert.Throws<ReglaDeNegocioException>(() => MotivoDeCambio.Create(largo));

            // Assert
            Assert.Equal("motivo", ex.Campo);
        }

        [Fact]
        public void Create_AcceptsAccentsAndEmojis()
        {
            // Arrange & Act
            var motivo = MotivoDeCambio.Create("Error de tipeo: el camión 🚚 pesó 1.250 L");

            // Assert
            Assert.Equal("Error de tipeo: el camión 🚚 pesó 1.250 L", motivo.Texto);
        }
    }
}
