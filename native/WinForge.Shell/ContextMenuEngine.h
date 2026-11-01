#pragma once
#include <windows.h>
#include <algorithm>
#include <string>
#include <vector>
#include <map>

namespace WinForgeMenu {
inline std::wstring Normalize(std::wstring text) {
    auto tab = text.find(L'\t'); if (tab != std::wstring::npos) text.resize(tab);
    text.erase(std::remove(text.begin(), text.end(), L'&'), text.end());
    for (auto suffix : {std::wstring(L"..."), std::wstring(L"\x2026")}) {
        auto pos = text.find(suffix); if (pos != std::wstring::npos) text.erase(pos, suffix.size());
    }
    auto first = text.find_first_not_of(L" \t\r\n"), last = text.find_last_not_of(L" \t\r\n");
    if (first == std::wstring::npos) return {};
    text = text.substr(first, last - first + 1);
    std::wstring result(text.size(), L'\0');
    LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_LOWERCASE, text.data(), static_cast<int>(text.size()), result.data(), static_cast<int>(result.size()), nullptr, nullptr, 0);
    return result;
}
inline std::wstring Canonical(std::wstring const& text, std::wstring const& parent = {}) {
    auto name = Normalize(text), scope = Normalize(parent);
    if (name == L"properties" || name == L"\x00f6zellikler" || name == L"ozellikler") return L"builtin:properties";
    if (name == L"new folder" || name == L"yeni klas\x00f6r" || ((scope == L"new" || scope == L"yeni") && (name == L"folder" || name == L"klas\x00f6r"))) return L"builtin:new-folder";
    if (name == L"new" || name == L"yeni") return L"builtin:new";
    if (name == L"copy" || name == L"kopyala") return L"builtin:copy";
    if (name == L"cut" || name == L"kes") return L"builtin:cut";
    if (name == L"paste" || name == L"yap\x0131\x015ft\x0131r") return L"builtin:paste";
    if (name == L"delete" || name == L"sil") return L"builtin:delete";
    if (name == L"rename" || name == L"yeniden adland\x0131r") return L"builtin:rename";
    if (name == L"refresh" || name == L"yenile") return L"builtin:refresh";
    return {};
}
struct Rule { std::wstring match, name, group, icon; bool visible{true}; };
struct Item {
    HMENU menu{}; UINT position{}; MENUITEMINFOW info{}; std::wstring text, parent;
    MENUITEMINFOW Data() const { auto copy = info; copy.dwTypeData = const_cast<wchar_t*>(text.c_str()); copy.cch = static_cast<UINT>(text.size()); return copy; }
};
inline std::vector<Item> ReadItems(HWND owner, HMENU menu, bool populate, std::wstring const& parent = {}, unsigned depth = 0) {
    std::vector<Item> result; if (depth > 4 || !IsMenu(menu)) return result;
    auto count = GetMenuItemCount(menu);
    for (int i = 0; i < count; ++i) {
        wchar_t label[2048]{}; Item item; item.menu = menu; item.position = static_cast<UINT>(i); item.parent = parent;
        item.info.cbSize = sizeof(item.info);
        item.info.fMask = MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_SUBMENU | MIIM_CHECKMARKS | MIIM_DATA | MIIM_BITMAP | MIIM_STRING;
        item.info.dwTypeData = label; item.info.cch = static_cast<UINT>(std::size(label));
        if (!GetMenuItemInfoW(menu, static_cast<UINT>(i), TRUE, &item.info)) continue;
        item.text = label; item.info.dwTypeData = nullptr; result.push_back(item);
        if (item.info.hSubMenu) {
            // Only New needs early initialization to promote its Folder command.
            // Opening unrelated extension submenus can be expensive or contact remote locations.
            if (populate && owner && Canonical(item.text) == L"builtin:new") SendMessageW(owner, WM_INITMENUPOPUP, reinterpret_cast<WPARAM>(item.info.hSubMenu), MAKELPARAM(i, FALSE));
            auto children = ReadItems(owner, item.info.hSubMenu, populate, item.text, depth + 1);
            result.insert(result.end(), children.begin(), children.end());
        }
    }
    return result;
}
struct Changed { Item original; bool removed{}; };
class Transaction {
    HMENU root{};
    std::vector<Changed> changes;
    std::map<std::wstring, HMENU> groups;
    std::vector<HBITMAP> bitmaps;
    std::vector<HMENU> menus;
public:
    ~Transaction() { Restore(); }
    bool IsGroup(HMENU menu) const { if (std::find(menus.begin(), menus.end(), menu) != menus.end()) return true; for (auto const& entry : groups) if (entry.second == menu) return true; return false; }
    void Apply(HMENU menu, std::vector<Item> const& items, std::vector<Rule> const& rules) {
        root = menu;
        for (auto const& item : items) { menus.push_back(item.menu); if (item.info.hSubMenu) menus.push_back(item.info.hSubMenu); }
        for (auto cursor = items.rbegin(); cursor != items.rend(); ++cursor) {
            auto const& item = *cursor; if (item.text.empty() || (item.info.fType & MFT_SEPARATOR)) continue;
            auto canonical = Canonical(item.text, item.parent), normalized = Normalize(item.text);
            auto found = std::find_if(rules.begin(), rules.end(), [&](auto const& rule) { return rule.match == normalized || (!canonical.empty() && rule.match == canonical); });
            if (found == rules.end()) continue;
            auto data = item.Data();
            bool remove = !found->visible || !found->group.empty();
            if (remove && !RemoveMenu(item.menu, item.position, MF_BYPOSITION)) continue;
            changes.push_back({item, remove});
            if (!found->visible) continue;
            auto title = found->name.empty() ? item.text : found->name;
            data.dwTypeData = title.data(); data.cch = static_cast<UINT>(title.size());
            if (!found->icon.empty()) {
                auto bitmap = static_cast<HBITMAP>(LoadImageW(nullptr, found->icon.c_str(), IMAGE_BITMAP, 0, 0, LR_LOADFROMFILE | LR_CREATEDIBSECTION));
                if (bitmap) { data.hbmpItem = bitmap; bitmaps.push_back(bitmap); }
            }
            if (!found->group.empty()) {
                auto& group = groups[found->group];
                if (!group) { group = CreatePopupMenu(); AppendMenuW(root, MF_POPUP, reinterpret_cast<UINT_PTR>(group), found->group.c_str()); }
                // Reverse traversal preserves original positions while removing; insert at front to retain order.
                if (!InsertMenuItemW(group, 0, TRUE, &data)) { auto original = item.Data(); InsertMenuItemW(item.menu, item.position, TRUE, &original); changes.pop_back(); }
            } else SetMenuItemInfoW(item.menu, item.position, TRUE, &data);
        }
    }
    void Restore() {
        for (auto const& entry : groups) {
            auto group = entry.second;
            if (IsMenu(group)) while (GetMenuItemCount(group) > 0) RemoveMenu(group, 0, MF_BYPOSITION);
            if (IsMenu(root)) for (int i = GetMenuItemCount(root) - 1; i >= 0; --i) if (GetSubMenu(root, i) == group) RemoveMenu(root, static_cast<UINT>(i), MF_BYPOSITION);
            if (IsMenu(group)) DestroyMenu(group);
        }
        std::sort(changes.begin(), changes.end(), [](auto const& a, auto const& b) { if (a.original.menu != b.original.menu) return reinterpret_cast<UINT_PTR>(a.original.menu) < reinterpret_cast<UINT_PTR>(b.original.menu); return a.original.position < b.original.position; });
        for (auto const& change : changes) if (change.removed && IsMenu(change.original.menu)) { auto original = change.original.Data(); InsertMenuItemW(change.original.menu, change.original.position, TRUE, &original); }
        for (auto const& change : changes) if (!change.removed && IsMenu(change.original.menu)) { auto original = change.original.Data(); SetMenuItemInfoW(change.original.menu, change.original.position, TRUE, &original); }
        for (auto bitmap : bitmaps) DeleteObject(bitmap);
        changes.clear(); groups.clear(); bitmaps.clear(); menus.clear(); root = nullptr;
    }
};
}
