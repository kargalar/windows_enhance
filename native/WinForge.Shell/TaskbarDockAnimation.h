#pragma once
#include <chrono>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Media.Animation.h>
#include <winrt/Windows.UI.ViewManagement.h>

namespace winforge::shell {

// Owns only the icon transform. Layout, click targets and shell input stay native.
class TaskbarDockAnimation {
    winrt::weak_ref<FrameworkElement> button_;
    winrt::weak_ref<FrameworkElement> target_;
    XamlValue originalTransform_{nullptr};
    Data::BindingBase originalBinding_{nullptr};
    XamlValue originalOrigin_{nullptr};
    Data::BindingBase originalOriginBinding_{nullptr};
    Media::ScaleTransform scale_{nullptr};
    Media::Animation::Storyboard storyboard_{nullptr};
    XamlValue entered_{nullptr};
    XamlValue exited_{nullptr};
    XamlValue canceled_{nullptr};
    double factor_{};
    int milliseconds_{};

    static FrameworkElement FindIcon(DependencyObject const& parent, int depth = 0) {
        if (depth >= 16) return nullptr;
        auto count = Media::VisualTreeHelper::GetChildrenCount(parent);
        for (int index = 0; index < count; ++index) {
            auto child = Media::VisualTreeHelper::GetChild(parent, index);
            if (auto element = child.try_as<FrameworkElement>(); element && element.Name() == L"Icon") return element;
            if (auto icon = FindIcon(child, depth + 1)) return icon;
        }
        return nullptr;
    }

    void Animate(bool hovering) noexcept {
        try {
            if (!scale_) return;
            auto target = target_.get();
            if (!target) return;
            auto animate = winrt::Windows::UI::ViewManagement::UISettings{}.AnimationsEnabled();
            auto value = hovering ? factor_ : 1.0;
            auto currentX = scale_.ScaleX();
            auto currentY = scale_.ScaleY();
            if (storyboard_) storyboard_.Stop();
            scale_.ScaleX(currentX);
            scale_.ScaleY(currentY);
            if (!animate) { scale_.ScaleX(value); scale_.ScaleY(value); return; }
            auto next = Media::Animation::Storyboard{};
            for (auto const* property : {L"ScaleX", L"ScaleY"}) {
                Media::Animation::DoubleAnimation animation;
                animation.To(value);
                animation.From(property == std::wstring_view{L"ScaleX"} ? currentX : currentY);
                animation.Duration(Duration{std::chrono::milliseconds(hovering ? milliseconds_ : milliseconds_ * 5 / 4)});
                Media::Animation::CubicEase easing;
                easing.EasingMode(Media::Animation::EasingMode::EaseOut);
                animation.EasingFunction(easing);
                animation.EnableDependentAnimation(true);
                Media::Animation::Storyboard::SetTarget(animation, scale_);
                Media::Animation::Storyboard::SetTargetProperty(animation, property);
                next.Children().Append(animation);
            }
            next.Begin();
            storyboard_ = next;
        } catch (...) { }
    }

public:
    TaskbarDockAnimation(FrameworkElement const& button, double factor, int milliseconds)
        : button_(winrt::make_weak(button)), factor_(factor), milliseconds_(milliseconds) {
        auto target = FindIcon(button);
        if (!target) target = button;
        target_ = winrt::make_weak(target);
        auto transformProperty = UIElement::RenderTransformProperty();
        originalTransform_ = target.ReadLocalValue(transformProperty);
        auto binding = target.GetBindingExpression(transformProperty);
        originalBinding_ = binding ? binding.ParentBinding() : nullptr;
        auto originProperty = UIElement::RenderTransformOriginProperty();
        originalOrigin_ = target.ReadLocalValue(originProperty);
        binding = target.GetBindingExpression(originProperty);
        originalOriginBinding_ = binding ? binding.ParentBinding() : nullptr;
        scale_ = Media::ScaleTransform{};
        // Preserve an existing shell transform by composing it with the hover transform.
        Media::TransformGroup group;
        if (auto original = target.RenderTransform()) group.Children().Append(original);
        group.Children().Append(scale_);
        target.RenderTransformOrigin({0.5f, 1.0f});
        target.RenderTransform(group);
        try {
            using PointerHandler = winrt::Windows::UI::Xaml::Input::PointerEventHandler;
            entered_ = winrt::box_value(PointerHandler{[this](auto const&, auto const&) { Animate(true); }});
            exited_ = winrt::box_value(PointerHandler{[this](auto const&, auto const&) { Animate(false); }});
            canceled_ = winrt::box_value(PointerHandler{[this](auto const&, auto const&) { Animate(false); }});
            button.AddHandler(UIElement::PointerEnteredEvent(), entered_, true);
            button.AddHandler(UIElement::PointerExitedEvent(), exited_, true);
            button.AddHandler(UIElement::PointerCanceledEvent(), canceled_, true);
        } catch (...) { Restore(); throw; }
    }

    bool Matches(double factor, int milliseconds) const noexcept {
        return factor_ == factor && milliseconds_ == milliseconds;
    }

    double Scale() const { return scale_ ? scale_.ScaleX() : 1.0; }

    ~TaskbarDockAnimation() noexcept { Restore(); }

private:
    void Restore() noexcept {
        try {
            if (auto button = button_.get()) {
                if (entered_) button.RemoveHandler(UIElement::PointerEnteredEvent(), entered_);
                if (exited_) button.RemoveHandler(UIElement::PointerExitedEvent(), exited_);
                if (canceled_) button.RemoveHandler(UIElement::PointerCanceledEvent(), canceled_);
            }
            if (storyboard_) storyboard_.Stop();
            if (auto target = target_.get()) {
                auto restore = [&](DependencyProperty const& property, XamlValue const& value, Data::BindingBase const& binding) {
                    if (binding) target.SetBinding(property, binding);
                    else if (value == DependencyProperty::UnsetValue()) target.ClearValue(property);
                    else target.SetValue(property, value);
                };
                restore(UIElement::RenderTransformProperty(), originalTransform_, originalBinding_);
                restore(UIElement::RenderTransformOriginProperty(), originalOrigin_, originalOriginBinding_);
            }
        } catch (...) { }
    }
};
}
