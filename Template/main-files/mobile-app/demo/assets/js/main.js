
$(function () {
  "use strict";

  /* switcher */

  $("#BlueTheme").on("click", function () {
    $("html").attr("data-bs-theme", "blue-theme")
  }),

  $("#LightTheme").on("click", function () {
    $("html").attr("data-bs-theme", "light")
  }),

    $("#DarkTheme").on("click", function () {
      $("html").attr("data-bs-theme", "dark")
    })


 /* searchbar */

 $(".btn-search").on("click", function () {
  $(".searchbar").removeClass("d-none")
})

$(".search-close").on("click", function () {
  $(".searchbar").addClass("d-none")
})



$(".btn-show-less").on("click", function () {
  $(".transaction-avatar").toggleClass("d-none")
})


    
});


    










