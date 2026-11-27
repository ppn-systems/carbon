// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// The shared menu visual language (docs/art/menu-ui.md): button states, field validation timing, Enter behaviour,
/// caps-lock inference, the show-password toggle, the strength meter and the palette's contrast.
/// </summary>
public sealed class MenuVisualLanguageTests
{
    private static readonly RectangleF s_buttonBounds = new(100f, 100f, 200f, 40f);
    private static readonly Vector2 s_inside = new(150f, 120f);
    private static readonly Vector2 s_outside = new(10f, 10f);

    [Fact]
    public void MenuButtonUpdate_WalksNormalHoverPressedAndClicks()
    {
        MenuButton button = new("OK") { Bounds = s_buttonBounds };

        button.Update(s_outside, leftDown: false, leftPressed: false).Should().BeFalse();
        button.State.Should().Be(MenuButtonVisualState.Normal);

        button.Update(s_inside, leftDown: false, leftPressed: false).Should().BeFalse("hovering is not a click");
        button.State.Should().Be(MenuButtonVisualState.Hovered);

        button.Update(s_inside, leftDown: true, leftPressed: true).Should().BeTrue("a press inside the button clicks it");
        button.State.Should().Be(MenuButtonVisualState.Pressed);

        button.Update(s_outside, leftDown: false, leftPressed: false);
        button.State.Should().Be(MenuButtonVisualState.Normal, "leaving the button drops hover and press");
    }

    [Fact]
    public void MenuButtonState_FocusShowsWhenNotHovered()
    {
        MenuButton button = new("OK") { Bounds = s_buttonBounds, IsFocused = true };
        button.Update(s_outside, leftDown: false, leftPressed: false);

        button.State.Should().Be(MenuButtonVisualState.Focused);
        button.HotAmount.Should().Be(1f, "keyboard focus lights the button like a hover");
    }

    [Fact]
    public void MenuButtonUpdate_WhenDisabledOrBusy_IgnoresClicksAndShowsThatState()
    {
        MenuButton disabled = new("OK") { Bounds = s_buttonBounds, IsEnabled = false, IsFocused = true };
        disabled.Update(s_inside, leftDown: true, leftPressed: true).Should().BeFalse("a disabled button cannot be clicked");
        disabled.State.Should().Be(MenuButtonVisualState.Disabled);

        MenuButton busy = new("OK") { Bounds = s_buttonBounds, IsBusy = true };
        busy.Update(s_inside, leftDown: true, leftPressed: true).Should().BeFalse("a busy button cannot be clicked twice");
        busy.State.Should().Be(MenuButtonVisualState.Busy, "busy wins over every other state");
    }

    [Fact]
    public void MenuButtonUpdate_EasesHoverOverItsDuration()
    {
        MenuButton button = new("OK") { Bounds = s_buttonBounds };
        button.Update(s_outside, false, false, dt: 0.016f);

        button.Update(s_inside, false, false, dt: MenuButton.HoverSeconds * 0.5f);
        button.HotAmount.Should().BeApproximately(0.5f, 0.001f, "the hover fade is half done after half its duration");

        button.Update(s_inside, false, false, dt: MenuButton.HoverSeconds);
        button.HotAmount.Should().Be(1f, "the fade ends fully lit and does not overshoot");

        button.Update(s_outside, false, false, dt: MenuButton.HoverSeconds * 0.25f);
        button.HotAmount.Should().BeApproximately(0.75f, 0.001f, "leaving fades back at the same rate");
    }

    [Fact]
    public void LoginFormEnter_InUsernameWithNoPassword_MovesToPasswordInsteadOfSending()
    {
        LoginForm form = new() { Username = "hero" };

        form.Enter().Should().Be(FormEnterResult.MovedFocus);
        form.Focus.Should().Be(LoginFocus.Password);

        form.Password = "secret1";
        form.Enter().Should().Be(FormEnterResult.Submit, "with both filled in, Enter sends");
    }

    [Fact]
    public void RegisterFormEnter_WalksToTheNextEmptyFieldThenSends()
    {
        RegisterForm form = new() { Username = "hero" };

        form.Enter().Should().Be(FormEnterResult.MovedFocus);
        form.Focus.Should().Be(RegisterFocus.Password);
        form.Password = "secret12";
        form.Enter().Should().Be(FormEnterResult.MovedFocus);
        form.Focus.Should().Be(RegisterFocus.Confirm);
        form.Confirm = "secret12";
        form.Enter().Should().Be(FormEnterResult.Submit);
    }

    [Fact]
    public void LoginFormErrors_EmptyFieldsShowOnlyAfterASendAttempt()
    {
        LoginForm form = new();
        form.Tab(reverse: false);
        form.Tab(reverse: false);

        form.VisibleUsernameError().Should().BeNull("an empty field is never flagged before the player tries to send");
        form.VisiblePasswordError().Should().BeNull();

        form.MarkSubmitAttempted();
        form.VisibleUsernameError().Should().Be("login.error.username_required");
        form.VisiblePasswordError().Should().Be("login.error.password_required");

        form.Username = "hero";
        form.VisibleUsernameError().Should().BeNull("a shown error clears as soon as the field is fixed");
    }

    [Fact]
    public void RegisterFormUsernameError_TooShortShowsOnlyAfterLeavingTheField()
    {
        RegisterForm form = new() { Username = "ab" };

        form.VisibleUsernameError().Should().BeNull("still typing: the player has not left the field");
        form.Tab(reverse: false);
        form.VisibleUsernameError().Should().Be("login.error.username_length", "punish late: after leaving the field");

        form.Username = "abc";
        form.VisibleUsernameError().Should().BeNull("reward early: the fix clears the error on the next keystroke");
    }

    [Fact]
    public void RegisterFormPasswordError_TooShortShowsAfterLeaving()
    {
        RegisterForm form = new() { Focus = RegisterFocus.Password, Password = "abc" };

        form.VisiblePasswordError().Should().BeNull();
        form.Focus = RegisterFocus.Confirm;
        form.VisiblePasswordError().Should().Be("login.error.password_length");
    }

    [Fact]
    public void RegisterFormConfirm_MismatchWaitsWhileItIsStillAPrefixButFlagsADivergence()
    {
        RegisterForm form = new() { Password = "secret12", Focus = RegisterFocus.Confirm, Confirm = "secr" };

        form.VisibleConfirmError().Should().BeNull("'secr' is still on its way to 'secret12'");
        form.PasswordsMatch.Should().BeFalse();

        form.Confirm = "secx";
        form.VisibleConfirmError().Should().Be("login.error.password_mismatch", "a character that differs can never match");

        form.Confirm = "secret12";
        form.VisibleConfirmError().Should().BeNull();
        form.PasswordsMatch.Should().BeTrue();
    }

    [Fact]
    public void RegisterFormConfirm_PrefixIsAMismatchOnceTheFieldIsLeftOrSent()
    {
        RegisterForm left = new() { Password = "secret12", Focus = RegisterFocus.Confirm, Confirm = "secr" };
        left.Focus = RegisterFocus.Submit;
        left.VisibleConfirmError().Should().Be("login.error.password_mismatch");

        RegisterForm sent = new() { Password = "secret12" };
        sent.MarkSubmitAttempted();
        sent.VisibleConfirmError().Should().Be("login.error.confirm_required", "an empty confirm is flagged on send");
    }

    [Theory]
    [InlineData('A', false, true)]
    [InlineData('a', true, true)]
    [InlineData('a', false, false)]
    [InlineData('A', true, false)]
    public void CapsLockTrackerObserveCharacter_InfersTheLockFromLetterCaseAndShift(char typed, bool shift, bool expectedOn)
    {
        CapsLockTracker tracker = new();
        tracker.ObserveCharacter(typed, shift);

        tracker.IsOn.Should().Be(expectedOn);
        tracker.IsKnown.Should().BeTrue();
    }

    [Fact]
    public void CapsLockTracker_IgnoresDigitsAndFlipsAKnownStateOnTheCapsKey()
    {
        CapsLockTracker tracker = new();
        tracker.ObserveCapsLockKey();
        tracker.IsKnown.Should().BeFalse("pressing Caps Lock before any letter tells nothing about the start state");

        tracker.ObserveCharacter('7', shiftDown: false);
        tracker.IsKnown.Should().BeFalse("digits have no case");

        tracker.ObserveCharacter('a', shiftDown: false);
        tracker.IsOn.Should().BeFalse();
        tracker.ObserveCapsLockKey();
        tracker.IsOn.Should().BeTrue("the key flips a known state");
    }

    [Fact]
    public void MenuTextFieldCapsWarning_ShowsOnlyOnAFocusedPasswordField()
    {
        MenuTextField password = new("P", "p", isPassword: true) { CapsLockOn = true };
        MenuTextField username = new("U", "u") { CapsLockOn = true, IsFocused = true };

        password.ShowsCapsLockWarning.Should().BeFalse("the warning clears when the password field loses focus");
        password.IsFocused = true;
        password.ShowsCapsLockWarning.Should().BeTrue();
        username.ShowsCapsLockWarning.Should().BeFalse("only password fields warn");
    }

    [Fact]
    public void MenuTextFieldToggleReveal_ChangesOnlyTheDisplayAndNeverWritesThePassword()
    {
        const string secret = "Tr0ub4dor&3";
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter captured = new();
        MenuTextField field = new("P", "p", isPassword: true) { IsFocused = true };
        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);
            foreach (char ch in secret)
            {
                field.AppendChar(ch);
            }

            field.DisplayText.Should().Be(new string('*', secret.Length), "a password is masked by default");
            field.ToggleReveal();
            field.IsRevealed.Should().BeTrue();
            field.DisplayText.Should().Be(secret);
            field.ToggleReveal();
            field.DisplayText.Should().Be(new string('*', secret.Length), "hiding again masks again");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        field.Text.Should().Be(secret, "revealing changes what is drawn, never what is stored");
        captured.ToString().Should().NotContain(secret, "the toggle must never log the password");
    }

    [Fact]
    public void MenuTextFieldToggleReveal_OnAPlainField_DoesNothing()
    {
        MenuTextField field = new("U", "u", "hero");
        field.ToggleReveal();

        field.IsRevealed.Should().BeFalse();
        field.RevealBounds.Should().Be(default(RectangleF), "only password fields have an eye");
    }

    [Theory]
    [InlineData("abc12", "TooShort")]
    [InlineData("aaaaaaaaaaaa", "Weak")]
    [InlineData("abcdefghij", "Weak")]
    [InlineData("987654321", "Weak")]
    [InlineData("xHeroRulesx", "Weak")]
    [InlineData("mossy7", "Weak")]
    [InlineData("mossyrock", "Fair")]
    [InlineData("mossyrock77x", "Good")]
    [InlineData("Mossy rock 7", "Strong")]
    [InlineData("a quiet mossy river", "Strong")]
    public void PasswordStrengthEvaluate_RatesLengthFirstAndPenalisesPatterns(string password, string expected) =>
        PasswordStrength.Evaluate(password, "herorules").ToString().Should().Be(expected);

    [Fact]
    public void MenuPalette_TextColoursMeetWcagAaOnTheFieldAndPanelFills()
    {
        ColorRgba[] texts = [MenuPalette.Placeholder, MenuPalette.ErrorText, MenuPalette.SuccessText, MenuPalette.WarningText, CarbonUiPalette.TextMuted, CarbonUiPalette.TextPrimary];
        ColorRgba[] fills = [MenuPalette.FieldFill, CarbonUiPalette.SlotSelectedFill, Opaque(CarbonUiPalette.WindowFill)];
        foreach (ColorRgba text in texts)
        {
            foreach (ColorRgba fill in fills)
            {
                MenuPalette.ContrastRatio(text, Opaque(fill)).Should().BeGreaterThanOrEqualTo(4.5, $"text {text} on {fill} must reach WCAG AA 4.5:1");
            }
        }

        MenuPalette.ContrastRatio(MenuPalette.FocusRing, Opaque(CarbonUiPalette.WindowFill)).Should().BeGreaterThanOrEqualTo(3.0, "the focus ring is a UI component (1.4.11)");
        MenuPalette.ContrastRatio(MenuPalette.ErrorText, Opaque(CarbonUiPalette.BorderSubtle)).Should().BeGreaterThanOrEqualTo(1.5, "an error border must stand apart from the normal border");
    }

    [Fact]
    public void MenuPaletteContrastRatio_MatchesWcagReferenceValues()
    {
        MenuPalette.ContrastRatio(ColorRgba.White, ColorRgba.Black).Should().BeApproximately(21.0, 0.01);
        MenuPalette.ContrastRatio(new ColorRgba(118, 118, 118, 255), ColorRgba.White).Should().BeApproximately(4.54, 0.01, "#767676 on white is the well-known 4.54:1 minimum grey");
    }

    private static ColorRgba Opaque(ColorRgba c) => c with { A = 255 };
}
