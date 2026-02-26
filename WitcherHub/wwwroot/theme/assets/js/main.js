$(function () {
    "use strict";

    // ===== Theme persistence (localStorage) =====
    (function () {
        const KEY = "wh_theme";
        const DEFAULT_THEME = "blue-theme";

        function getSavedTheme() {
            try { return localStorage.getItem(KEY); } catch { return null; }
        }

        function saveTheme(theme) {
            try { localStorage.setItem(KEY, theme); } catch { }
        }

        function applyTheme(theme) {
            const t = theme || DEFAULT_THEME;

            // طبّق الثيم
            $("html").attr("data-bs-theme", t);

            // تفعيل الراديو المناسب (لوحة الكوستمايز)
            $("#BlueTheme").prop("checked", t === "blue-theme");
            $("#LightTheme").prop("checked", t === "light");
            $("#DarkTheme").prop("checked", t === "dark");
            $("#SemiDarkTheme").prop("checked", t === "semi-dark");
            $("#BoderedTheme").prop("checked", t === "bodered-theme");

            // مزامنة أيقونة dark-mode إن كانت موجودة
            const icon = $(".dark-mode i");
            if (icon.length) {
                icon.text(t === "dark" ? "light_mode" : "dark_mode");
            }
        }

        // ملاحظة:
        // التطبيق الأساسي للثيم صار في <head> قبل تحميل CSS لمنع الفلاش.
        // هنا فقط نزامن الراديو/الأيقونة بعد ما الصفحة جاهزة.
        $(document).ready(function () {
            const current = $("html").attr("data-bs-theme");
            const saved = getSavedTheme();
            applyTheme(saved || current || DEFAULT_THEME);
        });

        // API عام للاستخدام في السويتشر
        window.WHTheme = {
            set(theme) {
                applyTheme(theme);
                saveTheme(theme);
            }
        };
    })();


    /* scrollar */
    new PerfectScrollbar(".notify-list");

    if (document.querySelector(".search-content")) {
        new PerfectScrollbar(".search-content");
    }
    // new PerfectScrollbar(".mega-menu-widgets")


    /* toggle button */
    $(".btn-toggle").click(function () {
        $("body").hasClass("toggled")
            ? ($("body").removeClass("toggled"), $(".sidebar-wrapper").unbind("hover"))
            : ($("body").addClass("toggled"),
                $(".sidebar-wrapper").hover(function () {
                    $("body").addClass("sidebar-hovered");
                }, function () {
                    $("body").removeClass("sidebar-hovered");
                }));
    });


    /* menu */
    (function () {
        $('#sidenav').metisMenu();
    })();

    $(".sidebar-close").on("click", function () {
        $("body").removeClass("toggled");
    });


    /* dark mode button (زر الدارك الرئيسي) */
    $(".dark-mode i").click(function () {
        $(this).text(function (i, v) {
            return v === 'dark_mode' ? 'light_mode' : 'dark_mode';
        });
    });

    $(".dark-mode").click(function () {
        const cur = $("html").attr("data-bs-theme");
        const next = (cur === "dark") ? "light" : "dark";
        window.WHTheme?.set(next);
    });


    /* sticky header */
    $(window).on("scroll", function () {
        if ($(this).scrollTop() > 60) {
            $('.top-header .navbar').addClass('sticky-header');
        } else {
            $('.top-header .navbar').removeClass('sticky-header');
        }
    });


    /* email */
    $(".email-toggle-btn").on("click", function () {
        $(".email-wrapper").toggleClass("email-toggled");
    });
    $(".email-toggle-btn-mobile").on("click", function () {
        $(".email-wrapper").removeClass("email-toggled");
    });
    $(".compose-mail-btn").on("click", function () {
        $(".compose-mail-popup").show();
    });
    $(".compose-mail-close").on("click", function () {
        $(".compose-mail-popup").hide();
    });


    /* chat */
    $(".chat-toggle-btn").on("click", function () {
        $(".chat-wrapper").toggleClass("chat-toggled");
    });
    $(".chat-toggle-btn-mobile").on("click", function () {
        $(".chat-wrapper").removeClass("chat-toggled");
    });


    /* switcher */
    $("#BlueTheme").on("click", function () {
        window.WHTheme?.set("blue-theme");
    });

    $("#LightTheme").on("click", function () {
        window.WHTheme?.set("light");
    });

    $("#DarkTheme").on("click", function () {
        window.WHTheme?.set("dark");
    });

    $("#SemiDarkTheme").on("click", function () {
        window.WHTheme?.set("semi-dark");
    });

    $("#BoderedTheme").on("click", function () {
        window.WHTheme?.set("bodered-theme");
    });


    /* search control */
    $(".search-control").click(function () {
        $(".search-popup").addClass("d-block");
        $(".search-close").addClass("d-block");
    });

    $(".search-close").click(function () {
        $(".search-popup").removeClass("d-block");
        $(".search-close").removeClass("d-block");
    });

    $(".mobile-search-btn").click(function () {
        $(".search-popup").addClass("d-block");
    });

    $(".mobile-search-close").click(function () {
        $(".search-popup").removeClass("d-block");
    });


    /* menu active */
    (function () {
        for (var e = window.location, o = $(".metismenu li a").filter(function () {
            return this.href == e;
        }).addClass("").parent().addClass("mm-active"); o.is("li");) {
            o = o.parent("").addClass("mm-show").parent("").addClass("mm-active");
        }
    })();

});