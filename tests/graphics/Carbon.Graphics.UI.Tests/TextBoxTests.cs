// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.Controls;
using FluentAssertions;
using Xunit;

namespace Carbon.Graphics.UI.Tests;

public sealed class TextBoxTests
{
    [Fact]
    public void AppendChar_WhenFocused_InsertsCharacterIntoBuffer()
    {
        // Arrange
        TextBox textBox = new(new RectangleF(0f, 0f, 200f, 30f))
        {
            IsFocused = true
        };

        // Act
        textBox.AppendChar('a');
        textBox.AppendChar('b');
        textBox.AppendChar('c');

        // Assert
        textBox.Text.Should().Be("abc");
    }

    [Fact]
    public void HandleBackspace_WhenTextPresent_RemovesLastCharacter()
    {
        // Arrange
        TextBox textBox = new(new RectangleF(0f, 0f, 200f, 30f))
        {
            IsFocused = true,
            Text = "hello"
        };

        // Act
        textBox.HandleBackspace();

        // Assert
        textBox.Text.Should().Be("hell");
    }

    [Fact]
    public void AppendChar_WhenMaxLengthReached_IgnoresAdditionalCharacters()
    {
        // Arrange
        TextBox textBox = new(new RectangleF(0f, 0f, 200f, 30f))
        {
            IsFocused = true,
            MaxLength = 3
        };

        // Act
        textBox.AppendChar('1');
        textBox.AppendChar('2');
        textBox.AppendChar('3');
        textBox.AppendChar('4');

        // Assert
        textBox.Text.Should().Be("123");
    }

    [Fact]
    public void HandleSubmit_WhenFocused_FiresSubmittedEventWithCurrentText()
    {
        // Arrange
        TextBox textBox = new(new RectangleF(0f, 0f, 200f, 30f))
        {
            IsFocused = true,
            Text = "login_user"
        };
        string submittedValue = string.Empty;
        textBox.Submitted += (_, text) => submittedValue = text;

        // Act
        textBox.HandleSubmit();

        // Assert
        submittedValue.Should().Be("login_user");
    }
}
