import { createRouter, createWebHistory } from 'vue-router'
import Placeholder from '../views/Placeholder.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: '/', name: 'placeholder', component: Placeholder }]
})

export default router
