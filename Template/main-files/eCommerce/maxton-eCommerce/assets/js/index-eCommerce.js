$(function () {
  "use strict";


// slider 1
var swiper = new Swiper(".main-slider", {
    loop: true,
    autoplay: {
      delay: 2500,
      disableOnInteraction: false,
    },
    navigation: {
      nextEl: ".main-slider-icon-right",
      prevEl: ".main-slider-icon-left",
    },
  });


  // slider 2
  var swiper = new Swiper(".Instagram-Swiper", {
    slidesPerView: 2,
    spaceBetween: 8,
    loop: true,
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
      },
      1200: {
        slidesPerView: 5,
        spaceBetween: 24,
      },
    },
  });


  // slider 3
  var swiper = new Swiper(".testimonial-swiper", {
    slidesPerView: 1,
    spaceBetween: 8,
    loop: true,
    breakpoints: {
      640: {
        slidesPerView: 1,
        spaceBetween: 24,

      },
      768: {
        slidesPerView: 1,
        spaceBetween: 24,
      },
      1024: {
        slidesPerView: 2,
        spaceBetween: 24,
      },
      1200: {
        slidesPerView: 3,
        spaceBetween: 24,
      },
    },
  });


  // slider 4
  var swiper = new Swiper(".brands-swiper", {
    slidesPerView: 1,
    spaceBetween: 8,
    loop: true,
    breakpoints: {
      640: {
        slidesPerView: 2,
        spaceBetween: 24,

      },
      768: {
        slidesPerView: 2,
        spaceBetween: 24,
      },
      1024: {
        slidesPerView: 3,
        spaceBetween: 24,
      },
      1200: {
        slidesPerView: 4,
        spaceBetween: 24,
      },
    },
  });


  });