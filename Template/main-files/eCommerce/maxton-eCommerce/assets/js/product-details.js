
$(function () {
  "use strict";

// slider 1
var swiper = new Swiper(".product-images-swiper-thumbnail", {
  spaceBetween: 10,
  slidesPerView: 4,
  freeMode: true,
  watchSlidesProgress: true,
});
var swiper2 = new Swiper(".product-images-swiper", {
  spaceBetween: 10,
  navigation: {
    nextEl: ".swiper-button-next",
    prevEl: ".swiper-button-prev",
  },
  thumbs: {
    swiper: swiper,
  },
});



// slider 2
var swiper = new Swiper(".recommended-products-slider", {
slidesPerView: 2,
spaceBetween: 16,
loop: true,
navigation: {
    nextEl: ".recommended-products-slider-icon-left",
    prevEl: ".recommended-products-slider-icon-right",
  },
breakpoints: {
    640: {
    slidesPerView: 2,
    spaceBetween: 24,

    },
    768: {
    slidesPerView: 3,
    spaceBetween: 24,
    },
    1024: {
    slidesPerView: 4,
    spaceBetween: 24,
    }
},
});


});